using System.Runtime.CompilerServices;
using Core.Mathematics;
using Core.Spatial;
using Map.Grid;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;

namespace Core.PathfindingAStar
{
    public static class AgentMovement
    {
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 ComputePbdDisplacement(ref GridBlob grid, float3 pos, SeparationContext ctx, float deltaTime)
        {
            float3 pbdDisplacement = float3.zero;
            
            uint agentCode = MortonUtils.GetMorton2D(pos, ctx.SpatialCellSize);
            float checkRadiusSq = ctx.SeparationRadius * ctx.SeparationRadius;

            if (agentCode < ctx.CellStarts.Length)
            {
                int startIdx = ctx.CellStarts[(int)agentCode];
                if (startIdx != -1)
                {
                    pbdDisplacement += CheckNeighbors(ctx.SortedEntries, startIdx, 1, pos, checkRadiusSq, ctx.SeparationRadius, ctx.AgentCount);
                    pbdDisplacement += CheckNeighbors(ctx.SortedEntries, startIdx, -1, pos, checkRadiusSq, ctx.SeparationRadius, ctx.AgentCount);
                }

                var finalPOs = pos + pbdDisplacement * deltaTime;
                if (math.lengthsq(pbdDisplacement) > 0.001f && GridUtils.IsWallAtWorldPos(finalPOs, ref grid))
                {
                    pbdDisplacement = float3.zero;
                }
            }

            return pbdDisplacement * ctx.SeparationWeight;
        }

        //axis-aligned sliding
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 ComputeWallSliding(ref GridBlob grid, ref MoveSettings moveData, float3 pos, float3 nextPos, float deltaTime) 
        {
            if (!GridUtils.IsWallAtWorldPos(nextPos, ref grid)) return nextPos;

            var nextX = new float3(nextPos.x, pos.y, pos.z);
            var nextZ = new float3(pos.x, pos.y, nextPos.z);

            float3 result;
            if (!GridUtils.IsWallAtWorldPos(nextX, ref grid)) result = nextX;
            else if (!GridUtils.IsWallAtWorldPos(nextZ, ref grid)) result = nextZ;
            else result = pos;

            moveData.Velocity = (result - pos) / math.max(deltaTime, 0.001f);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 ComputeNextPos(ref MoveSettings moveData, float3 pos, float3 targetPos, float3 pbdDisplacement, float desiredSpeed, float deltaTime)
        {
            float3 dirToTarget = math.normalize(targetPos - pos + 0.001f);

            float3 targetVelocity = dirToTarget * desiredSpeed + pbdDisplacement;

            moveData.Velocity = math.lerp(moveData.Velocity, targetVelocity, deltaTime * moveData.Acceleration);

            float3 nextPos = pos + moveData.Velocity * deltaTime;
            return nextPos;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeDesiredSpeed(ref GridBlob grid, in MoveSettings moveData, float3 pos, float baseSpeed, float3 targetPos, float arrivalRadius)
        {
            float distXZ = math.distance(pos.xz, targetPos.xz);

            float smoothWeight = GridUtils.GetWeightBilinear(ref grid, pos);
            if (!math.isfinite(smoothWeight) || smoothWeight <= 0f) smoothWeight = 1f;

            float deltaY = targetPos.y - pos.y;
            float slope = deltaY / math.max(distXZ, 0.5f);

            var slopeMul = slope > 0
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
        public static float3 ComputeMovement(ref GridBlob grid, ref MoveSettings moveData, float3 pos, float3 nextPos, float deltaTime)
        {
            float3 finalPos = nextPos;
            float finalHeight = GridUtils.GetHeightBilinear(ref grid, finalPos);
            float alphaY = math.clamp(deltaTime * moveData.VerticalSmoothSpeed, 0f, 1f);
            finalPos.y = math.lerp(pos.y, finalHeight, alphaY);
            return finalPos;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static quaternion ComputeRotation(ref GridBlob grid, ref MoveSettings moveData, ref LocalTransform transform, float3 pos, float3 target, float3 dirXZ, float deltaTime)
        {
            float3 actualDir = math.normalize(moveData.Velocity + 0.001f);
            moveData.LookDir = math.lerp(moveData.LookDir, actualDir, math.saturate(deltaTime * moveData.RotSpeed));
            moveData.LookDir = math.normalize(moveData.LookDir);
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
            return math.slerp(transform.Rotation, targetRot, math.min(1f, deltaTime * moveData.RotSpeed));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float3 CheckNeighbors(NativeArray<MortonEntry> sortedEntries, int startIdx, int direction, float3 pos, float radiusSq, float separationRadius, int agentCount)
        {
            float3 totalPush = float3.zero;
            int count = 0;
            float r = separationRadius;
            for (int i = 1; i < 15; i++)
            {
                int curr = startIdx + (i * direction);
                if ((uint)curr >= (uint)agentCount) break;

                float3 neighborPos = sortedEntries[curr].Position;

                float3 diff = pos - neighborPos;
                float dSq = math.lengthsq(diff);

                if (dSq < radiusSq && dSq > 0.0001f)
                {
                    float invD = math.rsqrt(dSq);
                    totalPush += diff * (invD * r - 1f) * 0.5f;
                    if (++count >= 6) break;
                }
            }
            return totalPush;
        }
    }
}
