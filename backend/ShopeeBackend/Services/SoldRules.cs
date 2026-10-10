using System;

namespace ShopeeBackend.Services
{
    public readonly record struct SoldAssignment(long Historical, long Monthly);

    public readonly record struct TotalReadingResult(
        long SoldDelta,
        long PreviousSold,
        DateTime? LastTotalAt,
        bool BaselineMoved);

    public static class SoldRules
    {
        public static SoldAssignment AssignLabeled(bool hasMonthLabel, long value)
        {
            if (value <= 0) return new SoldAssignment(0, 0);
            return hasMonthLabel
                ? new SoldAssignment(0, value)
                : new SoldAssignment(value, 0);
        }

        public static SoldAssignment AssignUnlabeled(long historical, long monthly)
        {
            if (historical > 0 && monthly > 0 && historical != monthly)
                return new SoldAssignment(historical, monthly);

            long value = historical > 0 ? historical : Math.Max(0, monthly);
            return new SoldAssignment(value, 0);
        }

        public static (long Historical, long Monthly) NormalizePayload(long historical, long monthly, long soldFallback)
        {
            if (historical > 0 && monthly > 0 && historical == monthly)
                monthly = 0;
            if (historical == 0 && monthly == 0 && soldFallback > 0)
                historical = soldFallback;
            return (historical, monthly);
        }

        public static TotalReadingResult ApplyTotalReading(
            long oldHistorical,
            long previousSold,
            long currentDelta,
            DateTime? lastTotalAt,
            long newHistorical,
            DateTime now,
            int minDays)
        {
            if (newHistorical <= 0)
                return new TotalReadingResult(currentDelta, previousSold, lastTotalAt, false);

            // Chưa từng chốt mốc tổng (dữ liệu cũ không biết số đang lưu là tổng hay tháng).
            if (oldHistorical <= 0 || !lastTotalAt.HasValue)
                return new TotalReadingResult(0, newHistorical, now, true);

            int window = minDays <= 0 ? 7 : minDays;
            double days = (now - lastTotalAt.Value).TotalDays;
            if (days < window)
                return new TotalReadingResult(currentDelta, previousSold, lastTotalAt, false);

            long baseline = previousSold > 0 ? previousSold : oldHistorical;
            if (newHistorical < baseline)
                return new TotalReadingResult(0, baseline, lastTotalAt, false);

            long delta = newHistorical > baseline ? newHistorical - baseline : 0;
            return new TotalReadingResult(delta, newHistorical, now, true);
        }

        public static long KeepDeltaOnMonthScan(long currentDelta) => currentDelta;
    }
}
