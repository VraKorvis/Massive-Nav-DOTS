using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Core.PathfindingAStar
{
    public struct BinaryHeapNode
    {
        public readonly int2 Position;
        public readonly float ExpectedCost;
        public readonly float DistanceToGoal;

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
        [NativeDisableUnsafePtrRestriction] private BinaryHeapNode* _buffer;
        private Allocator _allocator;
        private int _capacity;
        private int _length;
        private int _padding;

        public bool IsCreated
        {
            get => _buffer != null;
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private AtomicSafetyHandle m_Safety;
#endif

        public NativeBinaryMinHeap(int capacity, Allocator allocator)
        {
            if (allocator <= Allocator.None)
                throw new ArgumentException("Allocator must be Temp, TempJob or Persistent", nameof(allocator));
            if (capacity < 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be >= 0");

            _allocator = allocator;
            _capacity = capacity;
            _length = 0;
            _padding = 0;
            
            var size = (long)UnsafeUtility.SizeOf<BinaryHeapNode>() * capacity;
            _buffer = (BinaryHeapNode*)UnsafeUtility.Malloc(size, UnsafeUtility.AlignOf<BinaryHeapNode>(), allocator);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = AtomicSafetyHandle.Create();
#endif
        }

        public void Push(BinaryHeapNode node)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
            if (_length >= _capacity) throw new IndexOutOfRangeException("Heap Capacity Reached");
#endif
            int idx = _length;
            _buffer[idx] = node;
            _length++;

            // Sift Up
            while (idx > 0)
            {
                int parent = (idx - 1) / 2;
                if (_buffer[idx].ExpectedCost >= _buffer[parent].ExpectedCost) break;

                var temp = _buffer[idx];
                _buffer[idx] = _buffer[parent];
                _buffer[parent] = temp;
                idx = parent;
            }
        }

        public BinaryHeapNode Pop()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
            if (_length <= 0) throw new IndexOutOfRangeException("Heap is empty");
#endif
            BinaryHeapNode result = _buffer[0];
            _length--;

            if (_length > 0)
            {
                _buffer[0] = _buffer[_length];
                int idx = 0;

                // Sift Down
                while (true)
                {
                    int left = idx * 2 + 1;
                    int right = idx * 2 + 2;
                    
                    if (left >= _length) break;

                    int smallest = left;
                    if (right < _length)
                    {
                        smallest = math.select(left, right, _buffer[right].ExpectedCost < _buffer[left].ExpectedCost);
                    }

                    if (_buffer[smallest].ExpectedCost >= _buffer[idx].ExpectedCost) break;

                    var temp = _buffer[idx];
                    _buffer[idx] = _buffer[smallest];
                    _buffer[smallest] = temp;
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
            _length = 0;
        }

        public bool HasNext()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            return _length > 0;
        }

        public NativeBinaryMinHeap Slice(int start, int sliceLength)
        {
            return new NativeBinaryMinHeap
            {
                _buffer = _buffer + start,
                _capacity = sliceLength,
                _length = 0,
                _allocator = Allocator.None,
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

            if (!UnsafeUtility.IsValidAllocator(_allocator))
            {
                return;
            }

            UnsafeUtility.Free(_buffer, _allocator);
            _buffer = null;
            _capacity = 0;
            _length = 0;
        }
    }
}
