using Core.PathfindingAStar;
using Features.OptRenderer;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    public class TechSimulationSettingsHUD : MonoBehaviour
    {
        [Header("Controls")]
        [SerializeField] private Slider SeparationRadiusSlider;
        [SerializeField] private Slider SeparationWeightSlider;
        [SerializeField] private Slider SpatialCellSizeSlider;
        [SerializeField] private Slider TargetJitterSlider;
        [SerializeField] private Slider DensitySlider;
        [SerializeField] private Toggle Enabled;

        private EntityManager _entityManager;
        private EntityQuery _navSettingsQuery;
        private EntityQuery _cullingSettingsQuery;
        
        private void Awake()
        {
            if (World.DefaultGameObjectInjectionWorld != null)
            {
                _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
                
                _navSettingsQuery = _entityManager.CreateEntityQuery(ComponentType.ReadWrite<NavigationSettings>());
                _cullingSettingsQuery = _entityManager.CreateEntityQuery(ComponentType.ReadWrite<CullingSettings>());
            }
        }
        
        private void OnEnable()
        {
            RefreshUI();

            SeparationRadiusSlider.onValueChanged.AddListener(HandleSeparationRadiusChanged);
            SeparationWeightSlider.onValueChanged.AddListener(HandleSeparationWeightChanged);
            SpatialCellSizeSlider.onValueChanged.AddListener(HandleSpatialCellSizeChanged);
            TargetJitterSlider.onValueChanged.AddListener(HandleTargetJitterChanged);
            
            if (DensitySlider != null)
                DensitySlider.onValueChanged.AddListener(HandleDensityChanged);
                
            if (Enabled != null)
                Enabled.onValueChanged.AddListener(HandleEnabledChanged);
        }

        private void OnDisable()
        {
            SeparationRadiusSlider.onValueChanged.RemoveListener(HandleSeparationRadiusChanged);
            SeparationWeightSlider.onValueChanged.RemoveListener(HandleSeparationWeightChanged);
            SpatialCellSizeSlider.onValueChanged.RemoveListener(HandleSpatialCellSizeChanged);
            TargetJitterSlider.onValueChanged.RemoveListener(HandleTargetJitterChanged);
            
            if (DensitySlider != null)
                DensitySlider.onValueChanged.RemoveListener(HandleDensityChanged);
                
            if (Enabled != null)
                Enabled.onValueChanged.RemoveListener(HandleEnabledChanged);
        }
        
        public void RefreshUI()
        {
            if (_entityManager == default) return;

            if (TryGetNavSettings(out NavigationSettings navSettings))
            {
                SeparationRadiusSlider.SetValueWithoutNotify(navSettings.SeparationRadius);
                SeparationWeightSlider.SetValueWithoutNotify(navSettings.SeparationWeight);
                SpatialCellSizeSlider.SetValueWithoutNotify(navSettings.SpatialCellSize);
                TargetJitterSlider.SetValueWithoutNotify(navSettings.TargetJitterRange);
            }

            if (TryGetCullingSettings(out CullingSettings cullingSettings))
            {
                if (DensitySlider != null)
                    DensitySlider.SetValueWithoutNotify(cullingSettings.MaxAntsPerCell); 
                if (Enabled != null)
                    Enabled.SetIsOnWithoutNotify(cullingSettings.EnableCulling); 
            }
        }
        
        #region Handlers

        private void HandleSeparationRadiusChanged(float value) => UpdateNavSettings(s => s.SeparationRadius = value);
        private void HandleSeparationWeightChanged(float value) => UpdateNavSettings(s => s.SeparationWeight = value);
        private void HandleSpatialCellSizeChanged(float value) => UpdateNavSettings(s => s.SpatialCellSize = value);
        private void HandleTargetJitterChanged(float value) => UpdateNavSettings(s => s.TargetJitterRange = (int)value);
        private void HandleDensityChanged(float value) => UpdateCullingSettings(s => s.MaxAntsPerCell = (int)value);
        private void HandleEnabledChanged(bool value) => UpdateCullingSettings(s => s.EnableCulling = value);

        #endregion
        
        private bool TryGetNavSettings(out NavigationSettings settings)
        {
            if (!_navSettingsQuery.IsEmpty)
            {
                settings = _navSettingsQuery.GetSingleton<NavigationSettings>();
                return true;
            }
            settings = default;
            return false;
        }
        
        private bool TryGetCullingSettings(out CullingSettings settings)
        {
            if (!_cullingSettingsQuery.IsEmpty)
            {
                settings = _cullingSettingsQuery.GetSingleton<CullingSettings>();
                return true;
            }
            settings = default;
            return false;
        }

        private void UpdateNavSettings(System.Action<NavigationSettings> updateAction)
        {
            if (_navSettingsQuery.IsEmpty) return;

            var settings = _navSettingsQuery.GetSingleton<NavigationSettings>();
            updateAction(settings);
            _navSettingsQuery.SetSingleton(settings);
        }

        private void UpdateCullingSettings(System.Action<CullingSettings> updateAction)
        {
            if (_cullingSettingsQuery.IsEmpty) return;

            var settings = _cullingSettingsQuery.GetSingleton<CullingSettings>();
            updateAction(settings);
            _cullingSettingsQuery.SetSingleton(settings);
        }
    }
}
