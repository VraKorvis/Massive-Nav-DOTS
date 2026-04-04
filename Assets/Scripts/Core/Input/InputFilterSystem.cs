using Unity.Entities;
using UnityEngine.EventSystems;

namespace Core.Input
{
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    public partial class InputFilterSystem : SystemBase
    {
        protected override void OnCreate()
        {
            EntityManager.CreateEntity(typeof(InputBlockStatus));
            RequireForUpdate<InputBlockStatus>();
        }

        protected override void OnUpdate()
        {
            bool blocked = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            SystemAPI.SetSingleton(new InputBlockStatus
            {
                IsBlocked = blocked
            });
        }
    }
}