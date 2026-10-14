using System;
using System.Collections.Generic;
using System.Linq;

namespace BlazorApp.Shared.PhotoShoot
{
    /// <summary>
    /// Non-destructive shooting-block upgrade. It reorders setups, adds missing shots, and adds references.
    /// It never deletes a shot, setup, reference, image id, or source URL.
    /// </summary>
    public static class PhotoShootPlanUpgrade
    {
        public const int CurrentPlanVersion = 1;
        public const string PortraitCloseUrl = "https://lukehammer.com/favicon-96x96.png";
        public const string PortraitWideUrl = "https://lukehammer.com/apple-icon.png";

        public const string MacKingGreetingUrl = "https://i0.wp.com/insidemagiccom.wpcomstaging.com/wp-content/uploads/2011/06/MacKing-e1307041144625.jpg";
        public const string MacKingCloseMatchUrl = "https://cdn.getyourguide.com/img/tour/6531ac1ca24915e3572e96ccb9441f25bb6e09cc9f7d8056057d5384b92bfbd9.png/155.jpg";
        public const string MacKingCloseMatchPage = "https://www.getyourguide.com/las-vegas-l58/las-vegas-mac-king-comedy-magic-show-at-excalibur-t400247/";
        public const string BradRossReachUrl = "https://www.sixflags.com/_next/image?q=75&url=https%3A%2F%2Fcdn.sanity.io%2Fimages%2Fbsnrdz4t%2Fproduction%2Fb2ca193f96b49e4e4f0c6b174c4aa9300c8482bc-1140x690.png%3Fw%3D1920%26h%3D1066%26q%3D80%26fit%3Dclip%26auto%3Dformat%26dpr%3D2&w=1920";
        public const string BradRossReachPage = "https://www.sixflags.com/kingsisland/shows/brad-ross-international-star-illusionist";
        public const string SillyBillyAlternateUrl = "https://sillymagic.com/cdn/shop/files/Silly_Billy_2.jpg?v=1684141015&width=1500";
        public const string SillyBillyAlternatePage = "https://sillymagic.com/pages/about-david-kaye";

        private const string FloorSetupKey = "26-floor-play";

        public static bool LooksLikeImageUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url) || !PhotoShootValidator.IsSafeUrl(url))
            {
                return false;
            }

            var value = url!.ToLowerInvariant();
            return value.Contains(".jpg") || value.Contains(".jpeg") || value.Contains(".png") || value.Contains(".webp") || value.Contains(".gif");
        }

        public static bool HasPhoto(PhotoShootDocument doc, string shotId)
        {
            return PhotoShootEditor.ReferencesForShot(doc, shotId).Any(r => r.HasImage || LooksLikeImageUrl(r.SourceUrl));
        }

        public static bool NeedsWork(PhotoShootDocument doc)
        {
            if (doc == null)
            {
                return false;
            }

            if (doc.PlanVersion < CurrentPlanVersion)
            {
                return true;
            }

            return doc.Shots.Any(s => !HasPhoto(doc, s.Id));
        }

        /// <summary>Returns true when the document changed.</summary>
        public static bool Apply(PhotoShootDocument doc, DateTime nowUtc)
        {
            PhotoShootEditor.Normalize(doc);
            var changed = false;
            changed |= EnsureFloorSetup(doc);
            changed |= EnsureShots(doc, nowUtc);
            changed |= MoveWeddingCardShot(doc);
            changed |= Arrange(doc);
            changed |= AttachReferences(doc, nowUtc);
            if (doc.PlanVersion != CurrentPlanVersion)
            {
                doc.PlanVersion = CurrentPlanVersion;
                changed = true;
            }

            if (changed)
            {
                PhotoShootEditor.Normalize(doc);
            }

            return changed;
        }

        private static bool EnsureFloorSetup(PhotoShootDocument doc)
        {
            var seed = PhotoShootSeeder.SetupKeyPrefix + FloorSetupKey;
            if (doc.DeletedSeedKeys.Contains(seed) || doc.Setups.Any(s => s.SeedKey == seed))
            {
                return false;
            }

            doc.Setups.Add(new PhotoSetup
            {
                Id = PhotoShootIds.New(),
                SeedKey = seed,
                Name = "White Seamless - Playful Floor Poses",
                Description = "Prop-free floor poses for childlike wonder, curiosity, and play. No toys, balloons, or tantrums.",
                Background = "White seamless paper with floor sweep, lit to pure white",
                Lighting = "Same white-background lighting as the standing setups. Lower the camera to Luke's eye level on the floor.",
                CameraPosition = "On a low tripod or the floor, 35–50mm, far enough back for the whole pose",
                Outfit = "Same stage outfit as the rest of the shoot. No glasses.",
                Props = new List<string>(),
                ChangeoverMinutes = 5,
                Notes = "Clear the floor of every prop before this block."
            });
            return true;
        }

        private static bool EnsureShots(PhotoShootDocument doc, DateTime nowUtc)
        {
            var changed = false;
            foreach (var seed in NewShots())
            {
                var seedKey = PhotoShootSeeder.ShotKeyPrefix + seed.Key;
                if (doc.DeletedSeedKeys.Contains(seedKey) || doc.Shots.Any(s => s.SeedKey == seedKey))
                {
                    continue;
                }

                var setupSeed = PhotoShootSeeder.SetupKeyPrefix + seed.SetupKey;
                if (doc.DeletedSeedKeys.Contains(setupSeed))
                {
                    continue;
                }

                var setup = doc.Setups.FirstOrDefault(s => s.SeedKey == setupSeed);
                if (setup == null)
                {
                    continue;
                }

                var siblings = doc.Shots.Where(s => s.SetupId == setup.Id).ToList();
                doc.Shots.Add(new PhotoShot
                {
                    Id = PhotoShootIds.New(),
                    ShootId = doc.Id,
                    SetupId = setup.Id,
                    Sequence = siblings.Count == 0 ? 1 : siblings.Max(s => s.Sequence) + 1,
                    SeedKey = seedKey,
                    Title = seed.Title,
                    Summary = seed.Summary,
                    Objective = seed.Objective,
                    MarketingPurpose = seed.Marketing,
                    Category = seed.Category,
                    Priority = ShotPriority.Standard,
                    Framing = seed.Framing,
                    FacialExpression = seed.Expression,
                    BodyPosition = seed.Body,
                    HandPosition = seed.Hands,
                    EyeDirection = seed.Eyes,
                    PhotographerInstructions = seed.Photographer,
                    PerformerInstructions = seed.Performer,
                    Orientation = seed.Orientation,
                    NegativeSpace = seed.Space,
                    Props = string.IsNullOrWhiteSpace(seed.Props) ? new List<string>() : new List<string> { seed.Props },
                    Inspiration = seed.Inspiration,
                    EstimatedMinutes = 3,
                    UpdatedUtc = nowUtc
                });
                changed = true;
            }

            return changed;
        }

        private static bool MoveWeddingCardShot(PhotoShootDocument doc)
        {
            var shot = doc.Shots.FirstOrDefault(s => s.SeedKey == PhotoShootSeeder.ShotKeyPrefix + "burgundy-wedding-elegance");
            var cards = doc.Setups.FirstOrDefault(s => s.SeedKey == PhotoShootSeeder.SetupKeyPrefix + "14-white-magic-cards");
            if (shot == null || cards == null || shot.SetupId == cards.Id)
            {
                return false;
            }

            shot.SetupId = cards.Id;
            return true;
        }

        private static bool Arrange(PhotoShootDocument doc)
        {
            var before = doc.Setups
                .OrderBy(s => s.Sequence)
                .ThenBy(s => s.Id, StringComparer.Ordinal)
                .Select(s => s.Id + ":" + s.ShootingBlock + ":" + s.BlockName)
                .ToList();
            var map = BlockOrder();
            var unknownIndex = 0;
            foreach (var setup in doc.Setups.OrderBy(s => s.Sequence).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList())
            {
                var key = Strip(setup.SeedKey, PhotoShootSeeder.SetupKeyPrefix);
                if (map.TryGetValue(key, out var place))
                {
                    setup.ShootingBlock = place.Block;
                    setup.BlockName = place.Name;
                    setup.Sequence = place.Sort;
                }
                else
                {
                    unknownIndex++;
                    setup.Sequence = 10000 + unknownIndex;
                }
            }

            PhotoShootPlanner.Resequence(doc);
            var after = doc.Setups
                .OrderBy(s => s.Sequence)
                .ThenBy(s => s.Id, StringComparer.Ordinal)
                .Select(s => s.Id + ":" + s.ShootingBlock + ":" + s.BlockName)
                .ToList();
            return !before.SequenceEqual(after);
        }

        private static bool AttachReferences(PhotoShootDocument doc, DateTime nowUtc)
        {
            var changed = false;
            changed |= LinkUrl(doc, nowUtc, "goofy-close-up-greeting", MacKingGreetingUrl, "Mac King goofy close-up greeting", "Mac King",
                "Lean in, scrunch the body, open right hand beside the face, fingers spread, goofy smile.",
                "Do not copy the red plaid suit. Use Luke's stage outfit. No glasses.",
                ReferenceProvenance.ExactUserProvided, "");
            changed |= LinkUrl(doc, nowUtc, "confused-magician-seated", MacKingCloseMatchUrl, "Mac King scratching his head (close crop)", "Mac King",
                "Puzzled expression and the hand near the hairline.",
                "This crop is a close match, not the full pose. Keep him seated on a case. The public photo is not an exact replacement.",
                ReferenceProvenance.ClosePublicMatch, MacKingCloseMatchPage);
            changed |= LinkUrl(doc, nowUtc, "welcoming-invitation-open-hand", BradRossReachUrl, "Brad Ross welcoming reach", "Brad Ross",
                "Hand extended toward the viewer, welcoming showman expression, room for text.",
                "Close public match only. This is not the purple Join the Magic graphic. Use Luke's stage outfit.",
                ReferenceProvenance.ClosePublicMatch, BradRossReachPage);
            changed |= LinkUrl(doc, nowUtc, "goofball-thumbs-up", SillyBillyAlternateUrl, "Silly Billy character portrait (alternate)", "Silly Billy",
                "Exuberant comedy energy only.",
                "Alternate inspiration. This is not the White House thumbs-up. No oversized glasses. Luke's stage outfit.",
                ReferenceProvenance.AlternateInspiration, SillyBillyAlternatePage);
            changed |= LinkUrl(doc, nowUtc, "goofball-eyelids", SillyBillyAlternateUrl, "Silly Billy character portrait (alternate)", "Silly Billy",
                "Exaggerated silly face only.",
                "Alternate inspiration. This is not the eyelid-pull pose and it is not a balloon. No glasses. No balloon in Luke's photo.",
                ReferenceProvenance.AlternateInspiration, SillyBillyAlternatePage);

            changed |= LinkExistingOrMissing(doc, nowUtc, "confused-magician-seated", "Mac King seated on a case",
                new[] { "case", "mac" }, "Sitting on a case, scratching near the hairline. Not found as a public URL or an existing upload.");
            changed |= LinkExistingOrMissing(doc, nowUtc, "goofball-thumbs-up", "Silly Billy White House thumbs-up",
                new[] { "white house" }, "Double thumbs-up outside the White House. Not found as a public URL or an existing upload.");
            changed |= LinkExistingOrMissing(doc, nowUtc, "goofball-eyelids", "Silly Billy eyelid pull",
                new[] { "eyelid" }, "Pulling down the lower eyelids, orange balloon in the reference. Not found as a public URL or an existing upload. Do not use a balloon.");
            changed |= LinkExistingOrMissing(doc, nowUtc, "promotional-double-point", "Brad Ross double point",
                new[] { "brad", "double point" }, "Both hands pointing to upper-left and lower-left empty space on white. Not found as a public URL or an existing upload.");
            changed |= LinkExistingOrMissing(doc, nowUtc, "welcoming-invitation-open-hand", "Brad Ross Join the Magic graphic",
                new[] { "join the magic" }, "Exact purple Join the Magic graphic. The Six Flags photo is only a close match.");
            changed |= LinkExistingOrMissing(doc, nowUtc, "welcoming-invitation-reach", "John Stevens welcoming reach",
                new[] { "john stevens" }, "Left hand reaching out on a green background. Not found as a public URL or an existing upload.");

            foreach (var shot in doc.Shots.ToList())
            {
                if (HasPhoto(doc, shot.Id))
                {
                    continue;
                }

                changed |= EnsurePortrait(doc, nowUtc, PortraitCloseUrl, "Luke cartoon portrait, close", "Use Luke's face and plaid-vest look as the subject. This is his existing site portrait, not a pose reference.");
                changed |= EnsurePortrait(doc, nowUtc, PortraitWideUrl, "Luke cartoon portrait, with cards", "Use Luke's face, vest, and playful magician look as the subject. This is his existing site portrait, not a pose reference.");
                changed |= AddShotToUrl(doc, shot.Id, PortraitCloseUrl);
                changed |= AddShotToUrl(doc, shot.Id, PortraitWideUrl);
            }

            return changed;
        }

        private static bool LinkUrl(PhotoShootDocument doc, DateTime nowUtc, string shotKey, string imageUrl, string title, string who, string imitate, string change, ReferenceProvenance provenance, string pageUrl)
        {
            var shot = FindShot(doc, shotKey);
            if (shot == null)
            {
                return false;
            }

            var changed = EnsureUrlReference(doc, nowUtc, imageUrl, title, who, imitate, change, provenance);
            if (!string.IsNullOrWhiteSpace(pageUrl))
            {
                changed |= EnsureUrlReference(doc, nowUtc, pageUrl, title + " (page)", who, imitate, change, provenance);
            }

            changed |= AddShotToUrl(doc, shot.Id, imageUrl);
            if (!string.IsNullOrWhiteSpace(pageUrl))
            {
                changed |= AddShotToUrl(doc, shot.Id, pageUrl);
            }

            return changed;
        }

        private static bool LinkExistingOrMissing(PhotoShootDocument doc, DateTime nowUtc, string shotKey, string title, string[] needles, string missingNote)
        {
            var shot = FindShot(doc, shotKey);
            if (shot == null)
            {
                return false;
            }

            var existing = doc.References.FirstOrDefault(r =>
                r.Provenance != ReferenceProvenance.ClosePublicMatch
                && r.Provenance != ReferenceProvenance.AlternateInspiration
                && r.Provenance != ReferenceProvenance.SubjectPortrait
                && r.Provenance != ReferenceProvenance.MissingOriginal
                && r.Provenance != ReferenceProvenance.ExactUserProvided
                && needles.All(n => Haystack(r).IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0));
            if (existing != null)
            {
                return AddShot(existing, shot.Id);
            }

            var marker = doc.References.FirstOrDefault(r => string.Equals(r.Title, title, StringComparison.OrdinalIgnoreCase));
            if (marker == null)
            {
                doc.References.Add(new PhotoReference
                {
                    Id = PhotoShootIds.New(),
                    ShotIds = new List<string> { shot.Id },
                    Title = title,
                    Provenance = ReferenceProvenance.MissingOriginal,
                    WhatToImitate = missingNote,
                    WhatToChange = "Do not invent a stand-in URL. Keep looking for the original.",
                    CreatedUtc = nowUtc,
                    UpdatedUtc = nowUtc
                });
                return true;
            }

            return AddShot(marker, shot.Id);
        }

        private static bool EnsurePortrait(PhotoShootDocument doc, DateTime nowUtc, string url, string title, string imitate)
        {
            return EnsureUrlReference(doc, nowUtc, url, title, "Lucky Luke", imitate, "This is a subject portrait, not a pose to copy.", ReferenceProvenance.SubjectPortrait);
        }

        private static bool EnsureUrlReference(PhotoShootDocument doc, DateTime nowUtc, string url, string title, string who, string imitate, string change, ReferenceProvenance provenance)
        {
            if (doc.References.Any(r => string.Equals(r.SourceUrl, url, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            doc.References.Add(new PhotoReference
            {
                Id = PhotoShootIds.New(),
                Title = title,
                SourceUrl = url,
                PerformerOrCharacter = who,
                WhatToImitate = imitate,
                WhatToChange = change,
                Provenance = provenance,
                CreatedUtc = nowUtc,
                UpdatedUtc = nowUtc
            });
            return true;
        }

        private static bool AddShotToUrl(PhotoShootDocument doc, string shotId, string url)
        {
            var matches = doc.References.Where(r => string.Equals(r.SourceUrl, url, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0 || matches.Any(r => r.ShotIds != null && r.ShotIds.Contains(shotId)))
            {
                return false;
            }

            var room = matches.FirstOrDefault(r => (r.ShotIds?.Count ?? 0) < PhotoShootValidator.MaxListItems);
            if (room != null)
            {
                return AddShot(room, shotId);
            }

            var template = matches[0];
            doc.References.Add(new PhotoReference
            {
                Id = PhotoShootIds.New(),
                ShotIds = new List<string> { shotId },
                Title = template.Title,
                SourceUrl = template.SourceUrl,
                PerformerOrCharacter = template.PerformerOrCharacter,
                WhatToImitate = template.WhatToImitate,
                WhatToChange = template.WhatToChange,
                Provenance = template.Provenance,
                Credit = template.Credit,
                CreatedUtc = template.CreatedUtc,
                UpdatedUtc = template.UpdatedUtc
            });
            return true;
        }

        private static bool AddShot(PhotoReference reference, string shotId)
        {
            reference.ShotIds ??= new List<string>();
            if (reference.ShotIds.Contains(shotId) || reference.ShotIds.Count >= PhotoShootValidator.MaxListItems)
            {
                return false;
            }

            reference.ShotIds.Add(shotId);
            return true;
        }

        private static PhotoShot? FindShot(PhotoShootDocument doc, string key) =>
            doc.Shots.FirstOrDefault(s => s.SeedKey == PhotoShootSeeder.ShotKeyPrefix + key);

        private static string Haystack(PhotoReference reference) =>
            string.Join(" ", new[] { reference.Title, reference.Credit, reference.PerformerOrCharacter, reference.SourceUrl });

        private static string Strip(string? seedKey, string prefix) =>
            (seedKey ?? "").StartsWith(prefix, StringComparison.Ordinal) ? seedKey!.Substring(prefix.Length) : seedKey ?? "";

        private static Dictionary<string, (int Block, int Sort, string Name)> BlockOrder()
        {
            var rows = new (string Key, int Block, string Name)[]
            {
                ("01-white-tight-headshot", 1, "Tight headshots and expressions"),
                ("02-white-expressions-a", 1, "Tight headshots and expressions"),
                ("03-white-expressions-b", 1, "Tight headshots and expressions"),
                ("22-teal-reactions", 1, "Tight headshots and expressions"),
                ("04-white-left-space", 2, "Waist-up advertising gestures"),
                ("05-white-right-space", 2, "Waist-up advertising gestures"),
                ("21-teal-family", 2, "Waist-up advertising gestures"),
                ("10-white-physical-comedy", 2, "Waist-up advertising gestures"),
                ("06-white-three-quarter", 3, "Three-quarter standing portraits"),
                ("09-white-wide-hero", 4, "Wide website and banner photographs"),
                ("07-white-full-length-portrait", 5, "Full-length portraits and comedy"),
                ("08-white-full-length-comedy", 5, "Full-length portraits and comedy"),
                ("17-white-empty-hands-magic", 6, "Empty-hand magic"),
                ("25-charcoal-seated-stool", 7, "Seated stool portraits"),
                (FloorSetupKey, 8, "Playful floor poses"),
                ("11-white-blank-sign", 9, "Signs and whiteboard props"),
                ("14-white-magic-cards", 10, "Magic props"),
                ("15-white-magic-wand-silk", 10, "Magic props"),
                ("16-white-magic-classic-props", 10, "Magic props"),
                ("18-white-absurd-props", 10, "Magic props"),
                ("19-white-absurd-gone-wrong", 10, "Magic props"),
                ("12-kids-volunteer-reactions", 11, "Children and volunteers"),
                ("13-kids-volunteer-trick", 11, "Children and volunteers"),
                ("20-white-vest-sleeves", 12, "Rolled-sleeve wardrobe"),
                ("24-burgundy-elegant", 3, "Three-quarter standing portraits")
            };

            var map = new Dictionary<string, (int Block, int Sort, string Name)>(StringComparer.Ordinal);
            for (var i = 0; i < rows.Length; i++)
            {
                map[rows[i].Key] = (rows[i].Block, (i + 1) * 10, rows[i].Name);
            }

            return map;
        }

        private static IEnumerable<ShotSeed> NewShots()
        {
            yield return Seed("goofy-close-up-greeting", "02-white-expressions-a", "Goofy Close-Up Greeting",
                "Lean toward the camera with a scrunched body, open right hand beside the face, fingers spread, and a goofy smile.",
                "A friendly comic greeting for social posts and the home page.", "Social clips; website greeting; show opener.",
                "Close-up", "Goofy friendly smile", "Leaning forward, body slightly scrunched", "Open right hand beside the face, fingers spread",
                "Into the lens", "Fill the frame with the face and the hand. Keep the fingers inside the frame.",
                "Greet a kid in the front row.", "Mac King", ShotCategory.FacialStorytelling, CameraOrientation.Vertical, NegativeSpaceDirection.None, "");
            yield return Seed("welcoming-invitation-reach", "04-white-left-space", "Welcoming Invitation - Reaching Hand",
                "Left hand reaches outward in a friendly come-on-in gesture, with room for text on the other side.",
                "An invitation image for ads and the home page.", "Home page; welcome ads; brochure cover.",
                "Waist-up", "Warm inviting smile", "Turned slightly, chest open", "Left hand reaching toward the viewer",
                "Into the lens", "Waist-up, hand fully in frame, clean white space opposite the hand.",
                "Invite the family in.", "John Stevens", ShotCategory.AdvertisingNegativeSpace, CameraOrientation.Horizontal, NegativeSpaceDirection.Right, "");
            yield return Seed("welcoming-invitation-open-hand", "04-white-left-space", "Welcoming Invitation - Open Hand",
                "Open hand extended toward the viewer, welcoming showman expression, negative space for a headline.",
                "A promotional welcome with room for type.", "Event ads; social headers; flyer.",
                "Waist-up", "Welcoming showman smile", "Square to camera", "One hand extended toward the lens",
                "Into the lens", "Keep the hand sharp and leave clean space for text. This is not the purple Join the Magic graphic.",
                "Welcome them to the show.", "Brad Ross", ShotCategory.AdvertisingNegativeSpace, CameraOrientation.Horizontal, NegativeSpaceDirection.Left, "");
            yield return Seed("promotional-double-point", "04-white-left-space", "Promotional Double Point",
                "Both hands point toward upper-left and lower-left empty space, friendly smile, white background.",
                "An advertising frame with two places for type.", "Flyers; web banners; social ads.",
                "Waist-up", "Friendly smile", "Square to camera", "One hand points up-left, the other down-left",
                "Into the lens", "Both hands in frame. Leave the empty space he points at clean and white.",
                "Point out the two best things about the party.", "Brad Ross", ShotCategory.AdvertisingNegativeSpace, CameraOrientation.Horizontal, NegativeSpaceDirection.Left, "");
            yield return Seed("confused-magician-seated", "25-charcoal-seated-stool", "Confused Magician",
                "Seated on a case, scratching near the hairline, playfully puzzled. This is not the standing confused headshot.",
                "A seated comic portrait of a magician who has lost the plot for a second.", "Comedy ads; about page; social.",
                "Seated three-quarter", "Playfully puzzled", "Sitting on a case, or the stool if no case is on set", "One hand scratches near the hairline",
                "Off camera, then back to the lens", "Keep the seated full pose. The public crop only matches the face and the hand, not the case.",
                "You just forgot which cup the ball is under.", "Mac King", ShotCategory.PhysicalComedy, CameraOrientation.Vertical, NegativeSpaceDirection.None, "Small case, if one is already on set");
            yield return Seed("goofball-thumbs-up", "10-white-physical-comedy", "Over-the-Top Goofball - Thumbs Up",
                "Double thumbs-up and an exuberant expression. No oversized glasses.",
                "A high-energy comedy frame.", "Social; birthday ads; show posters.",
                "Waist-up", "Exuberant open smile", "Leaning in", "Both thumbs up",
                "Into the lens", "Hands and face sharp. No glasses.",
                "You just heard the funniest joke of the year.", "Silly Billy", ShotCategory.PhysicalComedy, CameraOrientation.Vertical, NegativeSpaceDirection.None, "");
            yield return Seed("goofball-eyelids", "10-white-physical-comedy", "Over-the-Top Goofball - Silly Face",
                "Pull the lower eyelids down with the fingers and make an exaggerated silly face. No balloon and no glasses.",
                "A second goofball expression from the same waist-up setup.", "Social; comedy reel still.",
                "Close-up to waist-up", "Exaggerated silly face", "Leaning toward camera", "Fingers pulling the lower eyelids",
                "Into the lens", "No balloon, no glasses, no toys.",
                "Make a face that would make a six-year-old snort.", "Silly Billy", ShotCategory.FacialStorytelling, CameraOrientation.Vertical, NegativeSpaceDirection.None, "");

            yield return Floor("floor-crisscross", "Crisscross Applesauce", "Sit cross-legged with hands on the knees and an excited smile.");
            yield return Floor("floor-kicking-feet", "Kicking My Feet", "Lie on the stomach, chin in the hands, feet kicked up behind.");
            yield return Floor("floor-bashful", "Bashful Kid", "Knees pulled up, arms wrapped around them, mischievous smile.");
            yield return Floor("floor-reach", "Reach for the Camera", "Legs in a V, leaning forward with an inviting gesture.");
            yield return Floor("floor-wonder", "Wonder and Amazement", "Lie on the back and look upward with wide-eyed surprise.");
            yield return Floor("floor-curious", "Curious Kid", "Sit cross-legged, lean forward, chin resting on one hand.");
            yield return Floor("floor-greeting", "Floor-Level Playful Greeting", "Sit on the floor and give a welcoming wave with an expressive smile.");
        }

        private static ShotSeed Floor(string key, string title, string summary) =>
            Seed(key, FloorSetupKey, title, summary,
                "Childlike wonder, curiosity, and innocent play. No tantrum.",
                "Family ads; social; website play section.",
                "Full body on the floor", "Playful and warm", summary, "See the pose. No props in the hands.",
                "Into the lens, unless the pose looks up",
                "Camera low. Floor clear of every prop. No toys, balloons, teddy bears, or glasses.",
                "Play, don't perform a tantrum.", "", ShotCategory.PhysicalComedy, CameraOrientation.Both, NegativeSpaceDirection.None, "");

        private static ShotSeed Seed(string key, string setup, string title, string summary, string objective, string marketing, string framing, string expression, string body, string hands, string eyes, string photographer, string performer, string inspiration, ShotCategory category, CameraOrientation orientation, NegativeSpaceDirection space, string props) =>
            new ShotSeed
            {
                Key = key,
                SetupKey = setup,
                Title = title,
                Summary = summary,
                Objective = objective,
                Marketing = marketing,
                Framing = framing,
                Expression = expression,
                Body = body,
                Hands = hands,
                Eyes = eyes,
                Photographer = photographer,
                Performer = performer,
                Inspiration = inspiration,
                Category = category,
                Orientation = orientation,
                Space = space,
                Props = props
            };

        private sealed class ShotSeed
        {
            public string Key = "";
            public string SetupKey = "";
            public string Title = "";
            public string Summary = "";
            public string Objective = "";
            public string Marketing = "";
            public string Framing = "";
            public string Expression = "";
            public string Body = "";
            public string Hands = "";
            public string Eyes = "";
            public string Photographer = "";
            public string Performer = "";
            public string Inspiration = "";
            public ShotCategory Category;
            public CameraOrientation Orientation;
            public NegativeSpaceDirection Space;
            public string Props = "";
        }
    }
}
