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
        private bool _isInitialized;

        private delegate void RefAction<T>(ref T provider);

        private void Update()
        {
            if (!_isInitialized)
            {
                InitializeEcs();
            }
        }

        private void InitializeEcs()
        {
            if (World.DefaultGameObjectInjectionWorld == null) return;

            _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            _navSettingsQuery = _entityManager.CreateEntityQuery(ComponentType.ReadWrite<NavigationSettings>());
            _cullingSettingsQuery = _entityManager.CreateEntityQuery(ComponentType.ReadWrite<CullingSettings>());

            if (!_navSettingsQuery.IsEmpty && !_cullingSettingsQuery.IsEmpty)
            {
                RefreshUI();
                _isInitialized = true;
            }
        }

        private void OnEnable()
        {
            SeparationRadiusSlider.onValueChanged.AddListener(HandleSeparationRadiusChanged);
            SeparationWeightSlider.onValueChanged.AddListener(HandleSeparationWeightChanged);
            SpatialCellSizeSlider.onValueChanged.AddListener(HandleSpatialCellSizeChanged);
            TargetJitterSlider.onValueChanged.AddListener(HandleTargetJitterChanged);
            
            if (DensitySlider != null)
                DensitySlider.onValueChanged.AddListener(HandleDensityChanged);
                
            if (Enabled != null)
                Enabled.onValueChanged.AddListener(HandleEnabledChanged);

            if (_isInitialized) RefreshUI();
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

        private void RefreshUI()
        {
            if (_entityManager == default || !_entityManager.World.IsCreated) return;

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

        private void HandleSeparationRadiusChanged(float value) => UpdateNavSettings((ref NavigationSettings s) => s.SeparationRadius = value);
        private void HandleSeparationWeightChanged(float value) => UpdateNavSettings((ref NavigationSettings s) => s.SeparationWeight = value);
        private void HandleSpatialCellSizeChanged(float value) => UpdateNavSettings((ref NavigationSettings s) => s.SpatialCellSize = value);
        private void HandleTargetJitterChanged(float value) => UpdateNavSettings((ref NavigationSettings s) => s.TargetJitterRange = (int)value);
        private void HandleDensityChanged(float value) => UpdateCullingSettings((ref CullingSettings s) => s.MaxAntsPerCell = (int)value);
        private void HandleEnabledChanged(bool value) => UpdateCullingSettings((ref CullingSettings s) => s.EnableCulling = value);

        #endregion

        private bool TryGetNavSettings(out NavigationSettings settings)
        {
            if (_navSettingsQuery.HasSingleton<NavigationSettings>())
            {
                settings = _navSettingsQuery.GetSingleton<NavigationSettings>();
                return true;
            }
            settings = default;
            return false;
        }

        private bool TryGetCullingSettings(out CullingSettings settings)
        {
            if (_cullingSettingsQuery.HasSingleton<CullingSettings>())
            {
                settings = _cullingSettingsQuery.GetSingleton<CullingSettings>();
                return true;
            }
            settings = default;
            return false;
        }

        private void UpdateNavSettings(RefAction<NavigationSettings> updateAction)
        {
            if (!_navSettingsQuery.HasSingleton<NavigationSettings>()) return;

            var settings = _navSettingsQuery.GetSingleton<NavigationSettings>();
            updateAction(ref settings);
            _navSettingsQuery.SetSingleton(settings);
        }

        private void UpdateCullingSettings(RefAction<CullingSettings> updateAction)
        {
            if (!_cullingSettingsQuery.HasSingleton<CullingSettings>()) return;

            var settings = _cullingSettingsQuery.GetSingleton<CullingSettings>();
            updateAction(ref settings);
            _cullingSettingsQuery.SetSingleton(settings);
        }
    }
}