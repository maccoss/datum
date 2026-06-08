// Minimal shim providing the single CollectionUtil.BinarySearch overload used by the vendored
// pwiz PeakFinding sources. Semantics match System.Collections.Generic.List<T>.BinarySearch:
// returns the index of the item, or the bitwise complement (~) of the insertion index if absent.
// The full pwiz CollectionUtil is large and not needed here.

using System.Collections.Generic;

namespace pwiz.Common.Collections
{
    internal static class CollectionUtil
    {
        public static int BinarySearch<TItem>(IList<TItem> list, TItem value)
            where TItem : System.IComparable<TItem>
        {
            int lo = 0;
            int hi = list.Count - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                int cmp = list[mid].CompareTo(value);
                if (cmp == 0)
                {
                    return mid;
                }

                if (cmp < 0)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return ~lo;
        }
    }
}
