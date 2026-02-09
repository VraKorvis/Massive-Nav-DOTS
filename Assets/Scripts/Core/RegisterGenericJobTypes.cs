using Unity.Collections;
using Unity.Jobs;

[assembly: RegisterGenericJobType(typeof(SortJob<PFStar.SortableRequest, PFStar.RequestComparer>))]
[assembly: RegisterGenericJobType(typeof(SortJob<PFStar.Morton.MortonEntry, PFStar.Morton.MortonEntryComparer>))]

