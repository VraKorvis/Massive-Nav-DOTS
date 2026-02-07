using System.Runtime.CompilerServices;
using Core.Mathematics;
using Gameplay.Player;
using Map;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace PFStar
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PathFindingSystem))]
    [BurstCompile]
    public partial struct PathMovementSystem : ISystem
    {
        private ComponentLookup<GridBlobReference> _gridBlobLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridTag>();
            _gridBlobLookup = state.GetComponentLookup<GridBlobReference>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _gridBlobLookup.Update(ref state);
            var gridEntity = SystemAPI.GetSingletonEntity<GridTag>();
            var gridBlobRef = _gridBlobLookup[gridEntity].Value;
            
            var moveJob = new PathMoveJob
            {
                GridBlob = gridBlobRef,
                DeltaTime = SystemAPI.Time.DeltaTime,
            };
            state.Dependency = moveJob.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        public partial struct PathMoveJob : IJobEntity
        {
            public float DeltaTime;
            [ReadOnly]
            public BlobAssetReference<GridBlob> GridBlob;
            
            private void Execute(
                ref DynamicBuffer<Waypoint> way,
                ref MoveSettings moveData,
                ref LocalTransform transform,
                ref PFAgentState agentState,
                in PlayerTag playerTag)
            {
                if (way.IsEmpty)
                {
                    StopAgent(ref agentState, ref moveData);
                    return;
                }

                ref var grid = ref GridBlob.Value;
                float3 pos = transform.Position;
                pos.y = GridUtils.GetHeightBilinear(ref grid, pos);
                transform.Position.y = pos.y;
                
                float arrivalRadius = math.max(0.05f, grid.CellSize * moveData.ArrivalRadiusFactor);
                
                TrimReachedWaypoints(way, pos, arrivalRadius);

                if (way.IsEmpty)
                {
                    StopAgent(ref agentState, ref moveData);
                    return;
                }

                float3 target = way[^1].point;
                target.y = GridUtils.GetHeightBilinear(ref grid, target);

                float distXZ = math.distance(pos.xz, target.xz);
                if (distXZ <= PhysConst.EPSILON_STABLE)
                {
                    way.RemoveAt(way.Length - 1);
                    return;
                }
                float3 moveDirXZ = math.normalize(new float3(target.x - pos.x, 0, target.z - pos.z));
                
                float desiredSpeed = CalculateDesiredSpeed(in moveData, ref grid, pos, moveData.Speed, target.y, distXZ, arrivalRadius);

                float3 nextPos = CalculateNextPosition(in moveData, ref grid, pos, moveDirXZ, desiredSpeed, grid.CellSize);

                ApplyMovement(ref moveData,  ref transform, pos, nextPos);

                ApplyRotation(in moveData, ref transform, ref grid, pos, target, moveDirXZ);

                moveData.TargetCellPos = target;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void StopAgent(ref PFAgentState state, ref MoveSettings move)
            {
                state.Flags = (byte)PFAgentStatus.Idle;
                move.Velocity = float3.zero;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private float CalculateDesiredSpeed(in MoveSettings moveData, ref GridBlob grid, float3 pos, float baseSpeed, float targetY, float distXZ, float arrivalRadius)
            {
                float smoothWeight = GridUtils.GetWeightBilinear(ref grid, pos);
                if (!math.isfinite(smoothWeight) || smoothWeight <= 0f) smoothWeight = 1f;

                float deltaY = targetY - pos.y;
                float slope = deltaY / math.max(distXZ, 0.5f); 
                
                float slopeMul = 1.0f;
                if (slope > 0) 
                {
                    slopeMul = math.lerp(1.0f, 0.2f, math.saturate(slope / moveData.MaxClimbRateFactor));
                }
                else 
                {
                    slopeMul = math.lerp(1.0f, 1.2f, math.saturate(-slope));
                }
                
                float weightMul = math.clamp(1.0f / math.max(PhysConst.EPSILON_WEIGHT, smoothWeight), moveData.MinSpeedMul, moveData.MaxSpeedMul);
                float speed = moveData.Speed * weightMul * slopeMul;
                
                float slowRadius = arrivalRadius * 2f;
                if (distXZ < slowRadius)
                {
                    float t = math.clamp((distXZ - arrivalRadius) / (slowRadius - arrivalRadius), 0f, 1f);
                    speed = math.max(speed * 0.3f, speed * t);
                }
                return math.max(speed, baseSpeed * moveData.MinSpeedMul);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private float3 CalculateNextPosition(in MoveSettings moveData, ref GridBlob grid, float3 pos, float3 dirXZ, float speed, float cellSize)
            {
                float step = math.clamp(speed * DeltaTime, math.max(0.01f, cellSize * 0.02f), 100f);
                float3 nextXZ = pos + dirXZ * step;

                float sampledH = GridUtils.GetHeightBilinear(ref grid, nextXZ);
                float maxDy = (moveData.MaxClimbRateFactor * cellSize) * DeltaTime;
                float allowedDy = math.clamp(sampledH - pos.y, -maxDy * 2f, maxDy);

                float alphaY = math.clamp(DeltaTime * moveData.VerticalSmoothSpeed, 0f, 1f);
                float newY = math.lerp(pos.y + allowedDy, sampledH, alphaY);

                return new float3(nextXZ.x, newY, nextXZ.z);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void ApplyMovement(ref MoveSettings moveData, ref LocalTransform transform, float3 pos, float3 nextPos)
            {
                float3 desiredVelocity = (nextPos - pos) / math.max(DeltaTime, PhysConst.EPSILON_STABLE);
                float blend = math.clamp(moveData.Acceleration * DeltaTime, 0f, 1f);

                moveData.Velocity = math.lerp(moveData.Velocity, desiredVelocity, blend);
                transform.Position += moveData.Velocity * DeltaTime;
                
                float alphaY = math.clamp(DeltaTime * moveData.VerticalSmoothSpeed, 0f, 1f);
                float finalHeight = GridUtils.GetHeightBilinear(ref GridBlob.Value, transform.Position);
                
                transform.Position.y = math.lerp(transform.Position.y, finalHeight, alphaY);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void ApplyRotation(in MoveSettings moveData, ref LocalTransform transform, ref GridBlob grid, float3 pos, float3 target, float3 dirXZ)
            {
                float3 nCur = GridUtils.GetNormalBilinear(ref grid, pos);
                float3 nTgt = GridUtils.GetNormalBilinear(ref grid, target);
                float3 surfaceNormal = math.normalize(math.lerp(nCur, nTgt, 0.5f));

                float3 tangent = dirXZ - surfaceNormal * math.dot(dirXZ, surfaceNormal);
                if (math.lengthsq(tangent) < PhysConst.EPSILON_STABLE) tangent = dirXZ;
                tangent = math.normalize(tangent);

                quaternion targetRot = quaternion.LookRotationSafe(tangent, surfaceNormal);
                transform.Rotation = math.slerp(transform.Rotation, targetRot, math.min(1f, DeltaTime * moveData.RotSpeed));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void TrimReachedWaypoints(DynamicBuffer<Waypoint> way, float3 pos, float arrivalRadius)
            {
                while (!way.IsEmpty)
                {
                    int last = way.Length - 1;
                    float3 p = way[last].point;
                    if (math.distance(pos.xz, p.xz) <= arrivalRadius)
                    {
                        way.RemoveAt(last);
                        continue;
                    }
                    break;
                }
            }
        }
    }
}
