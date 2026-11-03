using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BlazorApp.Shared.PhotoReview;
using FluentAssertions;

namespace UnitTest.PhotoReview;

public class PhotoLibraryDataTests
{
    private static readonly DateTime Generated = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    private static PhotoLibraryItem Item(string key, string categorySlug, double? rating, int ratingCount, bool current = true, int shootDay = 1, int width = 3000, int height = 2000) => new PhotoLibraryItem
    {
        PhotoId = key,
        PhotoKey = key,
        Title = "Shot " + key,
        Category = categorySlug == "headshots" ? "Headshots" : "Child Volunteer",
        CategorySlug = categorySlug,
        PoseKey = categorySlug + "/" + key,
        Rating = rating,
        RatingCount = ratingCount,
        IsCurrent = current,
        ShootId = "shoot-" + shootDay,
        ShootDate = "2026-06-" + shootDay.ToString("00"),
        ShootCreatedUtc = new DateTime(2026, 6, shootDay, 0, 0, 0, DateTimeKind.Utc),
        Files = { new PhotoLibraryFile { Kind = "CroppedWhiteBackground", Label = "cropped on white", FileName = key + ".jpg", Bytes = 2048 * 1024, Width = width, Height = height, Url = "u", DownloadUrl = "u?download=1" } }
    };

    [Fact]
    public void Parse_reads_known_filters_and_ignores_invalid_ones()
    {
        var query = PhotoLibraryData.Parse("?category=Headshots&minRating=8.5&current=false&sort=rating");
        query.Category.Should().Be("headshots");
        query.MinRating.Should().Be(8.5);
        query.Current.Should().BeFalse();
        query.Sort.Should().Be("rating");

        var invalid = PhotoLibraryData.Parse("?minRating=11&sort=bogus&current=all");
        invalid.MinRating.Should().BeNull();
        invalid.Current.Should().BeNull();
        invalid.Sort.Should().Be("category");
    }

    [Fact]
    public void Document_filters_by_category_rating_and_current()
    {
        var items = new List<PhotoLibraryItem>
        {
            Item("a", "headshots", 9.0, 3),
            Item("b", "headshots", null, 0),
            Item("c", "child-volunteer", 7.0, 2),
            Item("d", "headshots", 6.0, 4, current: false)
        };

        PhotoLibraryData.Document(items, PhotoLibraryData.Parse("?category=headshots"), Generated).Count.Should().Be(3);
        PhotoLibraryData.Document(items, PhotoLibraryData.Parse("?minRating=7"), Generated).Photos.Select(p => p.PhotoKey).Should().BeEquivalentTo("a", "c");
        PhotoLibraryData.Document(items, PhotoLibraryData.Parse("?current=false"), Generated).Photos.Should().ContainSingle().Which.PhotoKey.Should().Be("d");
    }

    [Fact]
    public void Sort_by_rating_puts_unrated_photos_last()
    {
        var items = new List<PhotoLibraryItem> { Item("low", "headshots", 5.0, 1), Item("none", "headshots", null, 0), Item("high", "headshots", 9.5, 2) };

        var document = PhotoLibraryData.Document(items, PhotoLibraryData.Parse("?sort=rating"), Generated);

        document.Photos.Select(p => p.PhotoKey).Should().Equal("high", "low", "none");
    }

    [Fact]
    public void Sort_by_newest_puts_the_latest_shoot_first()
    {
        var items = new List<PhotoLibraryItem> { Item("early", "headshots", 8, 1, shootDay: 2), Item("late", "headshots", 8, 1, shootDay: 20) };

        PhotoLibraryData.Document(items, PhotoLibraryData.Parse("?sort=newest"), Generated).Photos.Select(p => p.PhotoKey).Should().Equal("late", "early");
    }

    [Fact]
    public void Photo_reports_orientation_transparency_and_missing_values_as_null()
    {
        var item = Item("portrait", "headshots", null, 0, width: 2000, height: 3000);
        item.Files.Add(new PhotoLibraryFile { Kind = "CroppedTransparent", Label = "cropped transparent", FileName = "portrait.png", Bytes = 1, Width = 2000, Height = 3000, Url = "t", DownloadUrl = "t?download=1" });
        item.ShootDate = "";
        item.PoseSummary = "";

        var photo = PhotoLibraryData.Document(new[] { item }, new PhotoLibraryQuery(), Generated).Photos.Single();

        photo.Orientation.Should().Be("portrait");
        photo.HasTransparentVersion.Should().BeTrue();
        photo.Rating.Should().BeNull();
        photo.ShootDate.Should().BeNull();
        photo.PoseSummary.Should().BeNull();
        photo.Versions.Should().HaveCount(2);
        photo.Versions[0].WidthPx.Should().Be(2000);
    }

    [Fact]
    public void Document_describes_the_envelope_and_every_photo_field()
    {
        var items = new List<PhotoLibraryItem> { Item("a", "headshots", 9, 3) };

        var document = PhotoLibraryData.Document(items, new PhotoLibraryQuery { MinRating = 8 }, Generated);

        document.SchemaVersion.Should().Be(1);
        document.TotalPhotos.Should().Be(1);
        document.Count.Should().Be(1);
        document.Filters.MinRating.Should().Be(8);
        document.Filters.Category.Should().BeNull();

        var described = document.FieldDescriptions.Keys.ToHashSet();
        foreach (var property in typeof(LibraryPhoto).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            described.Should().Contain("photos[]." + char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1));
        }

        foreach (var property in typeof(LibraryVersion).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            described.Should().Contain("photos[].versions[]." + char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1));
        }
    }
}
