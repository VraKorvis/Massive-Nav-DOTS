using Unity.Collections;
using Unity.Jobs;

[assembly: RegisterGenericJobType(typeof(SortJob<Core.PathfindingAStar.PathRequestCandidate, Core.PathfindingAStar.RequestComparer>))]
[assembly: RegisterGenericJobType(typeof(SortJob<Core.Spatial.MortonEntry, Core.Spatial.MortonEntryComparer>))]