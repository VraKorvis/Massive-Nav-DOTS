using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Core.PathfindingAStar
{
    public struct BinaryHeapNode
    {
        public int2 Position;
        public float ExpectedCost;
        public float DistanceToGoal;

        public BinaryHeapNode(int2 position, float expectedCost, float distanceToGoal)
        {
            Position = position;
            ExpectedCost = expectedCost;
            DistanceToGoal = distanceToGoal;
        }
    }

    /// <summary>
    /// A native min heap implementation optimized for pathfinding with O(log n) push and pop.
    /// </summary>
    [NativeContainer]
    [NativeContainerSupportsDeallocateOnJobCompletion]
    public unsafe struct NativeBinaryMinHeap : IDisposable
    {
        [NativeDisableUnsafePtrRestriction] private BinaryHeapNode* buffer;
        private Allocator allocator;
        private int capacity;
        private int length;
        private int _padding;

        public bool IsCreated => buffer != null;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private AtomicSafetyHandle m_Safety;
#endif

        public NativeBinaryMinHeap(int capacity, Allocator allocator)
        {
            if (allocator <= Allocator.None)
                throw new ArgumentException("Allocator must be Temp, TempJob or Persistent", nameof(allocator));

            this.allocator = allocator;
            this.capacity = capacity;
            this.length = 0;
            _padding = 0;
            
            var size = (long)UnsafeUtility.SizeOf<BinaryHeapNode>() * capacity;
            buffer = (BinaryHeapNode*)UnsafeUtility.Malloc(size, UnsafeUtility.AlignOf<BinaryHeapNode>(), allocator);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = AtomicSafetyHandle.Create();
#endif
        }

        public void Push(BinaryHeapNode node)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
            if (length >= capacity) throw new IndexOutOfRangeException("Heap Capacity Reached");
#endif
            int idx = length;
            buffer[idx] = node;
            length++;

            // Sift Up
            while (idx > 0)
            {
                int parent = (idx - 1) / 2;
                if (buffer[idx].ExpectedCost >= buffer[parent].ExpectedCost) break;

                (buffer[idx], buffer[parent]) = (buffer[parent], buffer[idx]);
                idx = parent;
            }
        }

        public BinaryHeapNode Pop()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
            if (length <= 0) throw new IndexOutOfRangeException("Heap is empty");
#endif
            BinaryHeapNode result = buffer[0];
            length--;

            if (length > 0)
            {
                buffer[0] = buffer[length];
                int idx = 0;

                // Sift Down
                while (true)
                {
                    int left = idx * 2 + 1;
                    int right = idx * 2 + 2;
                    int smallest = idx;

                    if (left < length && buffer[left].ExpectedCost < buffer[smallest].ExpectedCost) smallest = left;
                    if (right < length && buffer[right].ExpectedCost < buffer[smallest].ExpectedCost) smallest = right;

                    if (smallest == idx) break;

                    (buffer[idx], buffer[smallest]) = (buffer[smallest], buffer[idx]);
                    idx = smallest;
                }
            }
            return result;
        }

        public void Clear()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            length = 0;
        }

        public bool HasNext()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            return length > 0;
        }

        public NativeBinaryMinHeap Slice(int start, int sliceLength)
        {
            return new NativeBinaryMinHeap
            {
                buffer = this.buffer + start,
                capacity = sliceLength,
                length = 0,
                allocator = Allocator.None,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = m_Safety
#endif
            };
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
            length = 0;
        }
    }
}
