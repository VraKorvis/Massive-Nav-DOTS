using Core.Gameplay;
using Core.PathfindingAStar;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Core.Visualizer
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class PlayerPathLineSystem : SystemBase    {
        
        protected override void OnUpdate()
        {

            if (PathLineBridge.Instance == null) return;
            bool playerPathActive = false;

            foreach (var (state, transform, entity) in 
                     SystemAPI.Query<RefRO<PFAgentState>, RefRO<LocalTransform>>()
                         .WithAll<PlayerTag>()
                         .WithEntityAccess())
            {
                var waypoints = SystemAPI.GetBuffer<Waypoint>(entity);

                if ((state.ValueRO.Flags & (byte)PFAgentStatus.Process) != 0 && waypoints.Length > 0)
                {
                    playerPathActive = true;
                    
                    Vector3[] positions = new  Vector3[waypoints.Length];
                    
                    for (int i = 1; i <= waypoints.Length; i++)
                    {
                        float3 wp = waypoints[i-1].point;
                        
                        positions[^i] = new Vector3(
                            wp.x, 
                            wp.y + 0.1f, 
                            wp.z  
                        );
                    }
                    
                    PathLineBridge.Instance.UpdatePath(positions);
                }
            }

            if (!playerPathActive)
            {
                PathLineBridge.Instance.ClearPath();
            }
        }
    }
}