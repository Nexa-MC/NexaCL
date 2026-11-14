using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition.Sidecars;

public sealed partial class SidecarClipboardApi
{
    private sealed class Binding : IDisposable
    {
        private readonly SidecarClipboardApi _api;
        private readonly CancellationTokenSource _stop = new();
        private bool _retired;
        internal HostClipboardActions Actions { get; }
        internal CancellationToken Lifetime { get; }
        internal Binding(SidecarClipboardApi api, HostClipboardActions actions)
        { _api = api; Actions = actions; Lifetime = _stop.Token; }
        public void Dispose()
        {
            Transfer[] transfers;
            lock (_api._gate)
            {
                if (_retired) return; _retired = true;
                if (ReferenceEquals(_api._binding, this)) _api._binding = null;
                transfers = _api._transfers.Where(value => ReferenceEquals(value.Binding, this)).ToArray();
            }
            try { _stop.Cancel(); }
            catch (Exception error) when (Nonfatal(error)) { }
            finally { foreach (var transfer in transfers) transfer.Retire(); _stop.Dispose(); }
        }
    }
    private sealed class Owner(SidecarClipboardApi api, SidecarHostSession session, CancellationToken lifetime)
    {
        internal SidecarClipboardApi Api { get; } = api;
        internal CancellationToken Lifetime { get; } = lifetime;
        internal readonly HashSet<Transfer> Transfers = [];
        internal long LastId;
        private bool _retired;
        private int _observed;
        private CancellationTokenRegistration _registration;
        internal bool IsActive => !_retired && !Lifetime.IsCancellationRequested && !Api._disposed;
        internal void Observe()
        {
            if (Interlocked.Exchange(ref _observed, 1) != 0) return;
            var registration = Lifetime.Register(Retire);
            bool retired;
            lock (Api._gate) { retired = _retired; if (!retired) _registration = registration; }
            if (retired) registration.Unregister();
        }
        internal void Retire()
        {
            Transfer[] transfers; CancellationTokenRegistration registration;
            lock (Api._gate)
            {
                if (_retired) return; _retired = true;
                Api._owners.Remove((session, Lifetime));
                registration = _registration; _registration = default;
                transfers = Transfers.ToArray();
            }
            registration.Unregister(); foreach (var transfer in transfers) transfer.Retire();
        }
    }
    private sealed class Transfer : IDisposable
    {
        private readonly Owner _owner;
        private readonly CancellationTokenSource _deadline, _stop;
        private CancellationTokenRegistration _registration;
        private int _retired;
        internal Binding Binding { get; }
        internal long Id { get; }
        internal bool IsRead { get; }
        internal CancellationToken Lifetime { get; }
        internal bool Retired => Volatile.Read(ref _retired) != 0;
        internal bool Ready, Working = true, NativeOperation, Committed;
        internal int Received;
        internal long Reservation, FinalReservation = -1;
        internal string? Text;
        internal byte[]? Image, Hash;
        internal TaskCompletionSource Work = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Transfer(Owner owner, Binding binding, long id, long bytes, bool read)
        {
            _owner = owner; Binding = binding; Id = id; Reservation = bytes; IsRead = read; NativeOperation = read;
            _deadline = new(TimeSpan.FromMinutes(2), owner.Api._clock);
            _stop = CancellationTokenSource.CreateLinkedTokenSource(owner.Lifetime, binding.Lifetime, _deadline.Token);
            Lifetime = _stop.Token;
        }
        internal void Observe()
        {
            var registration = Lifetime.Register(Retire);
            bool retired;
            lock (_owner.Api._gate) { retired = Retired; if (!retired) _registration = registration; }
            if (retired) registration.Unregister();
        }
        public void Dispose() => Retire();
        internal void FinishWork()
        {
            TaskCompletionSource completed;
            lock (_owner.Api._gate)
            {
                completed = Work; Working = false;
                if (NativeOperation) { NativeOperation = false; _owner.Api._nativeBusy = false; }
                if (FinalReservation >= 0)
                {
                    _owner.Api._reserved -= Reservation - FinalReservation;
                    Reservation = FinalReservation; FinalReservation = -1;
                }
            }
            completed.TrySetResult();
        }
        internal void Retire()
        {
            if (Interlocked.Exchange(ref _retired, 1) != 0) return;
            try { _stop.Cancel(); }
            catch (Exception error) when (Nonfatal(error)) { }
            finally
            {
                Task work;
                lock (_owner.Api._gate) work = Work.Task;
                _ = Cleanup(work);
            }
        }
        private async Task Cleanup(Task work)
        {
            // A cancelled native call can still be running. It keeps both its native
            // slot and storage reservation until the actual callback has returned.
            await work.ConfigureAwait(false);
            CancellationTokenRegistration registration;
            lock (_owner.Api._gate)
            {
                _owner.Api._reserved -= Reservation; Reservation = 0;
                _owner.Transfers.Remove(this); _owner.Api._transfers.Remove(this);
                Image = null; Hash = null; Text = null;
                registration = _registration; _registration = default;
            }
            registration.Unregister(); _stop.Dispose(); _deadline.Dispose();
        }
    }
}
