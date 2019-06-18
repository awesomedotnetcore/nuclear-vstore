﻿using System;

namespace CloningTool
{
    public sealed class CloningToolOptions
    {
        public CloneMode Mode { get; set; }

        public bool OverwriteUnequalRemarks { get; set; } = false;

        public int MaxDegreeOfParallelism { get; set; }

        public int MaxCloneTries { get; set; } = 3;

        public int TruncatedCloneSize { get; set; } = 5;

        public DateTime? AdvertisementsCreatedAtBeginDate { get; set; }

        public string AdvertisementIdsFilename { get; set; }

        public long? AdvertisementsProjectId { get; set; }

        public long? AdvertisementsTemplateId { get; set; }

        public string ApiVersion { get; set; }

        public int InitialPingTries { get; set; }

        public int InitialPingInterval { get; set; }
    }
}
