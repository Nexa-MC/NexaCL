namespace Nexa.Native.Pac;

public static partial class PacEngine
{
    private const string Helpers = """
        function isPlainHostName(h) { return h.indexOf('.') < 0; }
        function dnsDomainIs(h, d) { return h.endsWith(d); }
        function localHostOrDomainIs(h, d) { return h === d || (isPlainHostName(h) && d.startsWith(h + '.')); }
        function dnsDomainLevels(h) { return h.split('.').length - 1; }
        function isResolvable(h) { return dnsResolve(h) !== null; }
        function isInNet(h, p, m) {
            function ip(s) {
                var a = s.split('.'); if (a.length !== 4) return null;
                var n = 0; for (var i = 0; i < 4; i++) {
                    if (!/^\d+$/.test(a[i]) || +a[i] > 255) return null;
                    n = (n << 8) | +a[i];
                } return n;
            }
            var a = ip(h); if (a === null) { h = dnsResolve(h); if (h === null) return false; a = ip(h); }
            var b = ip(p), c = ip(m); return a !== null && b !== null && c !== null && (a & c) === (b & c);
        }
        function shExpMatch(s, p) {
            var i = 0, j = 0, star = -1, mark = 0;
            while (i < s.length) {
                if (j < p.length && (p[j] === '?' || p[j] === s[i])) { i++; j++; }
                else if (j < p.length && p[j] === '*') { star = j++; mark = i; }
                else if (star >= 0) { j = star + 1; i = ++mark; }
                else return false;
            }
            while (p[j] === '*') j++; return j === p.length;
        }
        function alert(s) { }
        function __pacRange(x, a, b) { return a <= b ? x >= a && x <= b : x >= a || x <= b; }
        function weekdayRange(a, b, z) {
            var days = ['SUN','MON','TUE','WED','THU','FRI','SAT'];
            var utc = b === 'GMT' || z === 'GMT', now = new Date();
            var lo = days.indexOf(a), hi = b === undefined || b === 'GMT' ? lo : days.indexOf(b);
            return lo >= 0 && hi >= 0 && __pacRange(utc ? now.getUTCDay() : now.getDay(), lo, hi);
        }
        function timeRange() {
            var a = Array.prototype.slice.call(arguments), utc = a[a.length - 1] === 'GMT'; if (utc) a.pop();
            var d = new Date(), h = utc ? d.getUTCHours() : d.getHours(), m = utc ? d.getUTCMinutes() : d.getMinutes(), s = utc ? d.getUTCSeconds() : d.getSeconds();
            if (a.length === 1) return h === a[0];
            if (a.length === 2) return __pacRange(h, a[0], a[1]);
            if (a.length === 4) return __pacRange(h * 60 + m, a[0] * 60 + a[1], a[2] * 60 + a[3]);
            if (a.length === 6) return __pacRange(h * 3600 + m * 60 + s, a[0] * 3600 + a[1] * 60 + a[2], a[3] * 3600 + a[4] * 60 + a[5]);
            return false;
        }
        function dateRange() {
            var a = Array.prototype.slice.call(arguments), utc = a[a.length - 1] === 'GMT'; if (utc) a.pop();
            var d = new Date(), day = utc ? d.getUTCDate() : d.getDate(), month = utc ? d.getUTCMonth() : d.getMonth(), year = utc ? d.getUTCFullYear() : d.getFullYear();
            var months = ['JAN','FEB','MAR','APR','MAY','JUN','JUL','AUG','SEP','OCT','NOV','DEC'];
            if (a.length === 1 || a.length === 2) {
                var x = typeof a[0] === 'string' ? month : a[0] > 31 ? year : day;
                var lo = typeof a[0] === 'string' ? months.indexOf(a[0]) : a[0];
                var hi = a.length === 1 ? lo : typeof a[1] === 'string' ? months.indexOf(a[1]) : a[1];
                return lo >= 0 && hi >= 0 && __pacRange(x, lo, hi);
            }
            if (a.length === 4 && typeof a[0] === 'string')
                return __pacRange(year * 12 + month, a[1] * 12 + months.indexOf(a[0]), a[3] * 12 + months.indexOf(a[2]));
            if (a.length === 4)
                return __pacRange(month * 32 + day, months.indexOf(a[1]) * 32 + a[0], months.indexOf(a[3]) * 32 + a[2]);
            if (a.length === 6)
                return __pacRange(year * 384 + month * 32 + day, a[2] * 384 + months.indexOf(a[1]) * 32 + a[0], a[5] * 384 + months.indexOf(a[4]) * 32 + a[3]);
            return false;
        }
        """;
}
