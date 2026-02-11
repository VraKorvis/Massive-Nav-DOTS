using Unity.Collections;
using Unity.Jobs;

[assembly: RegisterGenericJobType(typeof(SortJob<PFStar.PathRequestCandidate, PFStar.RequestComparer>))]
[assembly: RegisterGenericJobType(typeof(SortJob<PFStar.Morton.MortonEntry, PFStar.Morton.MortonEntryComparer>))]

