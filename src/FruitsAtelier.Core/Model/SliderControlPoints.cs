using System.Collections;
using System.Runtime.InteropServices;

namespace FruitsAtelier.Core;

public sealed class SliderControlPoints : IList<SliderPathPoint>, IReadOnlyList<SliderPathPoint>
{
    private readonly record struct PackedPoint(int X, int Y);
    private sealed class Buffer
    {
        public PackedPoint[] Packed = [];
        public SliderPathPoint[]? Precise;
        public bool Shared;
        public int Capacity => Precise?.Length ?? Packed.Length;
    }

    private Buffer buffer = new();
    private int count, version;
    public int Count => count;
    public bool IsReadOnly => false;
    public int Capacity
    {
        get => buffer.Capacity;
        set
        {
            if (value < count) throw new ArgumentOutOfRangeException(nameof(value));
            if (value == Capacity) return;
            ReplaceBuffer(value);
        }
    }

    public SliderPathPoint this[int index]
    {
        get
        {
            CheckIndex(index);
            if (buffer.Precise is { } precise) return precise[index];
            var point = buffer.Packed[index]; return new(point.X, point.Y);
        }
        set
        {
            CheckIndex(index); Prepare(count, value); Set(index, value); version++;
        }
    }

    internal SliderControlPoints Share()
    {
        // Published snapshots own separate wrappers; a shared buffer is never written again.
        buffer.Shared = true;
        return new() { buffer = buffer, count = count };
    }

    internal bool ContentEquals(SliderControlPoints other)
    {
        if (count != other.count) return false;
        if (ReferenceEquals(buffer, other.buffer)) return true;
        if (buffer.Precise is null && other.buffer.Precise is null)
            return MemoryMarshal.AsBytes(buffer.Packed.AsSpan(0, count)).SequenceEqual(MemoryMarshal.AsBytes(other.buffer.Packed.AsSpan(0, count)));
        if (buffer.Precise is { } left && other.buffer.Precise is { } right)
            return MemoryMarshal.AsBytes(left.AsSpan(0, count)).SequenceEqual(MemoryMarshal.AsBytes(right.AsSpan(0, count)))
                || left.AsSpan(0, count).SequenceEqual(right.AsSpan(0, count));
        for (int i = 0; i < count; i++) if (!this[i].Equals(other[i])) return false;
        return true;
    }

    public void Add(SliderPathPoint item)
    {
        Prepare(checked(count + 1), item); Set(count++, item); version++;
    }

    public void AddRange(IEnumerable<SliderPathPoint> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items is SliderControlPoints source && count == 0 && !ReferenceEquals(this, source))
        {
            source.buffer.Shared = true; buffer = source.buffer; count = source.count; version++; return;
        }
        if (ReferenceEquals(items, this)) items = this.ToArray();
        if (items.TryGetNonEnumeratedCount(out int extra)) EnsureCapacity(checked(count + extra));
        foreach (var point in items) Add(point);
    }

    public void Insert(int index, SliderPathPoint item)
    {
        if ((uint)index > (uint)count) throw new ArgumentOutOfRangeException(nameof(index));
        Prepare(checked(count + 1), item);
        if (buffer.Precise is { } precise) Array.Copy(precise, index, precise, index + 1, count - index);
        else Array.Copy(buffer.Packed, index, buffer.Packed, index + 1, count - index);
        Set(index, item); count++; version++;
    }

    public void InsertRange(int index, IEnumerable<SliderPathPoint> items)
    {
        if ((uint)index > (uint)count) throw new ArgumentOutOfRangeException(nameof(index));
        ArgumentNullException.ThrowIfNull(items);
        var added = items.ToArray();
        if (added.Length == 0) return;
        EnsureCapacity(checked(count + added.Length));
        foreach (var point in added) if (!CanPack(point)) { Promote(); break; }
        Detach();
        if (buffer.Precise is { } precise) Array.Copy(precise, index, precise, index + added.Length, count - index);
        else Array.Copy(buffer.Packed, index, buffer.Packed, index + added.Length, count - index);
        for (int i = 0; i < added.Length; i++) Set(index + i, added[i]);
        count += added.Length; version++;
    }

    public bool Remove(SliderPathPoint item)
    {
        int index = IndexOf(item); if (index < 0) return false; RemoveAt(index); return true;
    }
    public void RemoveAt(int index) { CheckIndex(index); RemoveRange(index, 1); }
    public void RemoveRange(int index, int length)
    {
        CheckRange(index, length);
        if (length == 0) return;
        Detach();
        if (buffer.Precise is { } precise) Array.Copy(precise, index + length, precise, index, count - index - length);
        else Array.Copy(buffer.Packed, index + length, buffer.Packed, index, count - index - length);
        count -= length; version++;
    }
    public int RemoveAll(Predicate<SliderPathPoint> match)
    {
        ArgumentNullException.ThrowIfNull(match);
        int target = 0, original = count;
        for (int i = 0; i < original; i++)
        {
            var point = this[i];
            if (match(point)) continue;
            if (target != i) { Detach(); Set(target, point); }
            target++;
        }
        if (target != count) { count = target; version++; }
        return original - target;
    }
    public void Clear()
    {
        if (count == 0) return;
        if (buffer.Shared) buffer = new();
        count = 0; version++;
    }
    public void Reverse() => Reverse(0, count);
    public void Reverse(int index, int length)
    {
        CheckRange(index, length); Detach();
        if (buffer.Precise is { } precise) Array.Reverse(precise, index, length);
        else Array.Reverse(buffer.Packed, index, length);
        version++;
    }
    public void Sort() => Sort(Comparer<SliderPathPoint>.Default);
    public void Sort(IComparer<SliderPathPoint>? comparer)
    {
        var points = this.ToArray(); Array.Sort(points, comparer); Clear(); AddRange(points);
    }
    public void Sort(Comparison<SliderPathPoint> comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        Sort(Comparer<SliderPathPoint>.Create(comparison));
    }
    public int IndexOf(SliderPathPoint item)
    {
        for (int i = 0; i < count; i++) if (this[i].Equals(item)) return i;
        return -1;
    }
    public bool Contains(SliderPathPoint item) => IndexOf(item) >= 0;
    public void CopyTo(SliderPathPoint[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        if (arrayIndex < 0 || arrayIndex > array.Length || count > array.Length - arrayIndex) throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        for (int i = 0; i < count; i++) array[arrayIndex + i] = this[i];
    }
    public int EnsureCapacity(int capacity)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (capacity > Capacity) ReplaceBuffer(Math.Max(capacity, Capacity == 0 ? 4 : checked(Capacity * 2)));
        return Capacity;
    }
    public IEnumerator<SliderPathPoint> GetEnumerator()
    {
        int expected = version;
        for (int i = 0; i < count; i++)
        {
            if (expected != version) throw new InvalidOperationException("Collection was modified.");
            yield return this[i];
        }
        if (expected != version) throw new InvalidOperationException("Collection was modified.");
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void Prepare(int capacity, SliderPathPoint item)
    {
        EnsureCapacity(capacity);
        if (!CanPack(item)) Promote();
        Detach();
    }
    private static bool CanPack(SliderPathPoint point) => Integer(point.X) && Integer(point.GeometryY);
    private static bool Integer(double value) => value >= int.MinValue && value <= int.MaxValue
        && value == Math.Truncate(value) && BitConverter.DoubleToInt64Bits(value) != long.MinValue;
    private void Set(int index, SliderPathPoint item)
    {
        if (buffer.Precise is { } precise) precise[index] = item;
        else buffer.Packed[index] = new((int)item.X, (int)item.GeometryY);
    }
    private void Detach() { if (buffer.Shared) ReplaceBuffer(Capacity); }
    private void ReplaceBuffer(int capacity)
    {
        var replacement = new Buffer();
        if (buffer.Precise is { } precise)
        {
            replacement.Precise = new SliderPathPoint[capacity]; Array.Copy(precise, replacement.Precise, count);
        }
        else
        {
            replacement.Packed = new PackedPoint[capacity]; Array.Copy(buffer.Packed, replacement.Packed, count);
        }
        buffer = replacement;
    }
    private void Promote()
    {
        if (buffer.Precise is not null) return;
        var precise = new SliderPathPoint[Capacity];
        for (int i = 0; i < count; i++) { var point = buffer.Packed[i]; precise[i] = new(point.X, point.Y); }
        buffer = new() { Precise = precise };
    }
    private void CheckIndex(int index) { if ((uint)index >= (uint)count) throw new ArgumentOutOfRangeException(nameof(index)); }
    private void CheckRange(int index, int length)
    {
        if (index < 0 || length < 0 || index > count - length) throw new ArgumentOutOfRangeException(nameof(index));
    }
}
