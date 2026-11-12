using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Services.Minecraft.Management;

public static partial class InstanceWorldService
{
    public static Task<XsrResult> RenameAsync(InstanceWorldRenameCommand command, XsrStateStore store, Func<string, IDisposable>? acquireSessionLock = null, CancellationToken token = default) => ExecuteAsync(
        command.InstanceDirectory, command.WorldName, command.ExpectedRevision, store, false, async (_, world, root, ct) =>
        {
            if (string.IsNullOrWhiteSpace(command.DisplayName) || command.DisplayName.Length > 256 || command.DisplayName.Any(char.IsControl)) throw new InvalidDataException("世界显示名称无效。");
            Data(root).SetString("LevelName", command.DisplayName);
            await WriteLevelAsync(world, root, command.ExpectedRevision, ct).ConfigureAwait(false);
        }, acquireSessionLock, token);

    public static Task<XsrResult> SetDataPackOrderAsync(InstanceWorldDataPackOrderCommand command, XsrStateStore store, Func<string, IDisposable>? acquireSessionLock = null, CancellationToken token = default) => ExecuteAsync(
        command.InstanceDirectory, command.WorldName, command.ExpectedRevision, store, false, async (_, world, root, ct) =>
        {
            RequireDataPacks(root);
            if (command.EnabledOrder.Count is < 1 or > 256 || command.EnabledOrder.Any(id => id.Length is 0 or > 512 || id.Any(char.IsControl))
                || command.EnabledOrder.Distinct(StringComparer.Ordinal).Count() != command.EnabledOrder.Count || !command.EnabledOrder.Contains("vanilla", StringComparer.Ordinal))
                throw new InvalidDataException("数据包顺序必须保留 vanilla，且不能包含重复条目。");
            var metadata = await ReadMetadataAsync(world, token: ct).ConfigureAwait(false);
            var available = metadata.DataPacks.Where(p => !p.Trashed).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            if (command.EnabledOrder.Any(id => !available.Contains(id))) throw new InvalidDataException("数据包不属于此世界。");
            foreach (string id in command.EnabledOrder.Where(id => id.StartsWith("file/", StringComparison.Ordinal)))
            {
                string name = id[5..]; if (!MinecraftVersionPaths.IsSafeReference(name)) throw new InvalidDataException("数据包名称无效。");
                string path = Path.Combine(world, "datapacks", name); RecoveryBlobStore.CheckLinks(path);
                if (!Path.Exists(path)) throw new IOException("数据包已不存在。");
            }
            var data = Data(root); var packs = data.Children.SingleOrDefault(t => t.Name == "DataPacks");
            if (packs is null) { packs = new(10, "DataPacks", []); data.Children.Add(packs); }
            if (packs.Type != 10) throw new InvalidDataException("数据包列表无效。");
            foreach (string key in PackLists)
            {
                var list = packs.Children.SingleOrDefault(t => t.Name == key);
                if (list is null) { list = new(9, key, []) { ListType = 8 }; packs.Children.Add(list); }
                if (list.Type != 9 || list.ListType is not (0 or 8)) throw new InvalidDataException("数据包列表无效。");
                list.ListType = 8; list.Children.Clear();
                IEnumerable<string> selected = key == "Enabled" ? command.EnabledOrder : available.Except(command.EnabledOrder, StringComparer.Ordinal).Order(StringComparer.Ordinal);
                foreach (string id in selected) { var holder = new ServerNbt(10, "", []); holder.SetString("", id); list.Children.Add(holder.Children[0]); }
            }
            await WriteLevelAsync(world, root, command.ExpectedRevision, ct).ConfigureAwait(false);
        }, acquireSessionLock, token);
}
