using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BlazorApp.Shared.PhotoShoot
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SessionAction
    {
        Start = 0,
        End = 1,
        SetCurrent = 2,
        Next = 3,
        Previous = 4,
        CompleteCurrent = 5,
        SkipCurrent = 6,
        CompleteShot = 7,
        SkipShot = 8,
        ReopenShot = 9,
        NextSetup = 10,
        RequestNext = 11,
        ClearNextRequest = 12,
        AddNote = 13,
        ShootNext = 14,
        SetPhotographerCanAdvance = 15,
        SetMustHaveOnly = 16
    }

    public class SessionCommand
    {
        public SessionAction Action { get; set; }
        public string ShotId { get; set; } = "";
        public string Text { get; set; } = "";
        public bool Flag { get; set; }
    }

    public class CreateShootRequest
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string ShootDate { get; set; } = "";
    }

    public class UpdateShootRequest
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string ShootDate { get; set; } = "";
        public int BreakEveryMinutes { get; set; }
        public int BreakDurationMinutes { get; set; }
        public bool MakeDefault { get; set; }
    }

    public class SetStatusRequest
    {
        public ShotStatus Status { get; set; }
    }

    public class FavoriteRequest
    {
        public bool IsFavorite { get; set; }
    }

    public class MoveShotRequest
    {
        /// <summary>-1 = up, +1 = down within the ordered list (crossing into neighbouring setups when at an edge).</summary>
        public int Delta { get; set; }
        /// <summary>When set, moves the shot to the end of this setup (empty string = unassigned) instead of using Delta.</summary>
        public string? TargetSetupId { get; set; }
    }

    public class MoveSetupRequest
    {
        public int Delta { get; set; }
    }

    public class DuplicateShootRequest
    {
        public string Name { get; set; } = "";
        public bool ResetProgress { get; set; } = true;
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ImportMode
    {
        /// <summary>Create a brand-new shoot with fresh ids (never touches existing data).</summary>
        CreateNew = 0,
        /// <summary>Replace an existing shoot's content. Requires ConfirmOverwrite.</summary>
        ReplaceExisting = 1
    }

    public class ImportRequest
    {
        public ImportMode Mode { get; set; }
        public string TargetShootId { get; set; } = "";
        public bool ConfirmOverwrite { get; set; }
        public PhotoShootExport? Export { get; set; }
    }

    public class ImportResult
    {
        public string ShootId { get; set; } = "";
        public int SetupCount { get; set; }
        public int ShotCount { get; set; }
        public int ReferenceCount { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class SeedResult
    {
        public string ShootId { get; set; } = "";
        public bool CreatedShoot { get; set; }
        public int AddedSetups { get; set; }
        public int AddedShots { get; set; }
        public int SkippedExisting { get; set; }
        public int SkippedDeleted { get; set; }
    }

    public class PhotoShootExport
    {
        public const string FormatName = "lucky-luke-photoshoot";
        public const int CurrentFormatVersion = 1;

        public string Format { get; set; } = FormatName;
        public int FormatVersion { get; set; } = CurrentFormatVersion;
        public DateTime ExportedUtc { get; set; }
        public PhotoShootDocument? Shoot { get; set; }
    }

    public class LiveShootResponse
    {
        public bool Changed { get; set; }
        public string ETag { get; set; } = "";
        public PhotoShootDocument? Shoot { get; set; }
        public string Message { get; set; } = "";
        /// <summary>Id of the item created by the request, when applicable.</summary>
        public string CreatedId { get; set; } = "";
    }
}
