using BlazorApp.Shared;
using FluentAssertions;

namespace UnitTest;

public class KidThemeColorsStorageMigrationTests
{
    [Fact]
    public void ToJson_removes_legacy_june_and_choen_theme_keys()
    {
        const string json = """
            {
              "backgroundByKid": {
                "Cohen": "#D8F3DC",
                "Choen": "#111111",
                "June": "#FBCFE8",
                "Levi": "#DBEAFE"
              }
            }
            """;

        var data = KidThemeColors.ParseJson(json);
        data.BackgroundByKid.Should().ContainKey("Cohen");
        data.BackgroundByKid.Should().ContainKey("Juniper");
        data.BackgroundByKid.Should().NotContainKey("June");
        data.BackgroundByKid.Should().NotContainKey("Choen");

        KidThemeColors.ToJson(data).Should().NotContain("\"June\"").And.NotContain("\"Choen\"");
        KidThemeColors.ToJson(data).Should().Contain("\"Juniper\"");
    }
}
