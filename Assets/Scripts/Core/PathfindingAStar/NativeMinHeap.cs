using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

/// <summary>
/// A native min heap implementation optimized for pathfinding.
/// Migrated to Unity 6 (Entities 1.x)
/// </summary>
[NativeContainer]
[NativeContainerSupportsDeallocateOnJobCompletion]
public unsafe struct NativeMinHeap : IDisposable
{
    [NativeDisableUnsafePtrRestriction] private MinHeapNode* buffer;
    private Allocator allocator;
    private int capacity;
    private int head;
    private int length;
    
#if ENABLE_UNITY_COLLECTIONS_CHECKS
    private AtomicSafetyHandle m_Safety;
#endif

    public bool IsCreated => buffer != null;

    public NativeMinHeap(int capacity, Allocator allocator)
    {
        var size = (long)UnsafeUtility.SizeOf<MinHeapNode>() * capacity;

        if (allocator <= Allocator.None)
        {
            throw new ArgumentException("Allocator must be Temp, TempJob or Persistent", nameof(allocator));
        }

        if (capacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Length must be >= 0");
        }

        if (size > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity),
                $"Length * sizeof(T) cannot exceed {int.MaxValue} bytes");
        }

        buffer = (MinHeapNode*)UnsafeUtility.Malloc(size, UnsafeUtility.AlignOf<MinHeapNode>(), allocator);
        this.capacity = capacity;
        this.allocator = allocator;
        this.head = -1;
        this.length = 0;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        m_Safety = AtomicSafetyHandle.Create();
        // AtomicSafetyHandle.SetAllowPreemptiveFree(m_Safety, true);
#endif
    }

    public bool HasNext()
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
        return head >= 0;
    }

    public void Push(MinHeapNode node)
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
        if (length == capacity)
        {
            throw new IndexOutOfRangeException("Capacity Reached");
        }
#endif
        MinHeapNode* nodes = buffer;

        if (head < 0)
        {
            head = length;
        }
        else if (node.ExpectedCost < buffer[head].ExpectedCost)
        {
            node.Next = head;
            head = length;
        }
        else
        {
            var currentPtr = head;
            while (nodes[currentPtr].Next >= 0 &&
                   nodes[nodes[currentPtr].Next].ExpectedCost <= node.ExpectedCost)
            {
                currentPtr = nodes[currentPtr].Next;
            }

            node.Next = nodes[currentPtr].Next;
            nodes[currentPtr].Next = length;
        }

        buffer[length] = node;
        length += 1;
    }

    public MinHeapNode Pop()
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
        int resultIndex = head;
        MinHeapNode resultNode = buffer[resultIndex];
        head = resultNode.Next;
        return resultNode;
    }

    public void Clear()
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
        head = -1;
        length = 0;
    }

    public void Dispose()
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle.Release(m_Safety);
#endif
        if (!UnsafeUtility.IsValidAllocator(allocator))
        {
            return;
        }

        UnsafeUtility.Free(buffer, allocator);
        buffer = null;
        capacity = 0;
    }

    public NativeMinHeap Slice(int start, int sliceLength)
    {
        var stride = UnsafeUtility.SizeOf<MinHeapNode>();

        return new NativeMinHeap
        {
            buffer = (MinHeapNode*)((byte*)buffer + (stride * start)),
            capacity = sliceLength,
            length = 0,
            head = -1,
            allocator = Allocator.None,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = m_Safety,
#endif
        };
    }

    private MinHeapNode Get(int index)
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        if (index < 0 || index >= length)
        {
            FailOutOfRangeError(index);
        }

        AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
        return UnsafeUtility.ReadArrayElement<MinHeapNode>(buffer, index);
    }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
    private void FailOutOfRangeError(int index)
    {
        throw new IndexOutOfRangeException($"Index {index} is out of range of '{capacity}' Length.");
    }
#endif
}

public struct MinHeapNode
{
    public int2 Position;
    public float ExpectedCost;
    public float DistanceToGoal;
    public int Next;

    public MinHeapNode(int2 position, float expectedCost, float distanceToGoal)
    {
        Position = position;
        ExpectedCost = expectedCost;
        DistanceToGoal = distanceToGoal;
        Next = -1;
    }
}