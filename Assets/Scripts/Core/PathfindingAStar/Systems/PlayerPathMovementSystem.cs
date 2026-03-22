using System.Runtime.CompilerServices;
using Core.Gameplay;
using Core.Mathematics;
using Map.Grid;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Core.PathfindingAStar
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PathFindingSystem))]
    [BurstCompile]
    public partial struct PlayerPathMovementSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridBlobReference>();
            state.RequireForUpdate<GridTag>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var gridBlobRef = SystemAPI.GetSingleton<GridBlobReference>().Value;

            var moveJob = new PathMoveJob
            {
                GridBlob = gridBlobRef,
                DeltaTime = state.WorldUnmanaged.Time.DeltaTime,
            };
            state.Dependency = moveJob.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        public partial struct PathMoveJob : IJobEntity
        {
            public float DeltaTime;
            [ReadOnly]
            public BlobAssetReference<GridBlob> GridBlob;

            [BurstCompile]
            private void Execute(
                ref DynamicBuffer<Waypoint> way,
                ref MoveSettings moveData,
                ref LocalTransform transform,
                ref PFAgentState agentState,
                in PlayerTag playerTag)
            {

                ref var grid = ref GridBlob.Value;

                float3 pos = transform.Position;
                float baseRadius = math.max(0.05f, grid.CellSize * moveData.ArrivalRadiusFactor);
                float arrivalRadius = (way.Length == 1) ? 0.05f : baseRadius;

                if (!TrimReachedWaypoints(way, pos, arrivalRadius))
                {
                    StopAgent(ref agentState, ref moveData);
                    return;
                }

                var groundHeight = GridUtils.GetHeightBilinear(ref grid, pos);
                pos.y = groundHeight + moveData.PivotOffset;
                transform.Position.y = pos.y;

                float3 targetPos = way[^1].point;
                targetPos.y = GridUtils.GetHeightBilinear(ref grid, targetPos);

                float3 moveDirXZ = math.normalize(new float3(targetPos.x - pos.x, 0, targetPos.z - pos.z));
                float distXZ = math.distance(pos.xz, targetPos.xz);
                float desiredSpeed = CalculateDesiredSpeed(in moveData, ref grid, pos, moveData.Speed, targetPos.y, distXZ, arrivalRadius);

                float3 nextPos = CalculateNextPosition(in moveData, ref grid, pos, moveDirXZ, desiredSpeed, grid.CellSize);

                ApplyMovement(ref moveData, ref transform, pos, nextPos);
                ApplyRotation(in moveData, ref transform, ref grid, pos, targetPos, moveDirXZ);
                moveData.TargetCellPos = targetPos;
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
                slopeMul = slope > 0
                    ? math.lerp(1.0f, moveData.MinSlopeMul, math.saturate(slope / moveData.MaxClimbRateFactor))
                    : math.lerp(1.0f, moveData.MaxSlopeMul, math.saturate(-slope));

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
                float distToTarget = math.distance(pos.xz, moveData.TargetCellPos.xz);
                float step = math.min(
                    math.clamp(speed * DeltaTime, math.max(0.01f, cellSize * 0.02f), 100f),
                    distToTarget);
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
                
                moveData.Velocity = math.lerp(moveData.Velocity, desiredVelocity, math.saturate(moveData.Acceleration * DeltaTime));
                
                float3 finalPos = pos + (moveData.Velocity * DeltaTime);

                float alphaY = math.clamp(DeltaTime * moveData.VerticalSmoothSpeed, 0f, 1f);
                float finalHeight = GridUtils.GetHeightBilinear(ref GridBlob.Value, transform.Position);
                finalPos.y = math.lerp(pos.y, finalHeight, alphaY);

                transform.Position = finalPos;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void ApplyRotation(in MoveSettings moveData, ref LocalTransform transform, ref GridBlob grid, float3 pos, float3 target, float3 dirXZ)
            {
                float3 nCur = GridUtils.GetNormalBilinear(ref grid, pos);
                float3 nTgt = GridUtils.GetNormalBilinear(ref grid, target);
                float3 surfaceNormal = math.normalize(math.lerp(nCur, nTgt, 0.5f));

                float3 tangent = dirXZ - surfaceNormal * math.dot(dirXZ, surfaceNormal);
                if (math.lengthsq(tangent) < PhysConst.EPSILON_STABLE)
                {
                    tangent = transform.Forward();
                }
                tangent = math.normalize(tangent);

                quaternion targetRot = quaternion.LookRotationSafe(tangent, surfaceNormal);
                transform.Rotation = math.slerp(transform.Rotation, targetRot, math.min(1f, DeltaTime * moveData.RotSpeed));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private bool TrimReachedWaypoints(DynamicBuffer<Waypoint> way, float3 pos, float arrivalRadius)
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
                    return true;
                }
                return false;
            }
        }
    }
}
