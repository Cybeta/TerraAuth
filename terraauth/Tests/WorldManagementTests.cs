using System.Text.Json;
using TerraAuth.Config;
using TerraAuth.Simulation;
using Xunit;

namespace TerraAuth.Tests;

public class WorldManagementTests
{
    [Fact]
    public void ListWorlds_ReturnsValidWorldAndMarksInvalidWorldUnavailable()
    {
        var directory = CreateDirectory();
        try
        {
            var manager = new WorldManager(Path.Combine(directory, "server.json"));
            Directory.CreateDirectory(manager.WorldsDirectory);
            WorldFileWriter.Write(Path.Combine(manager.WorldsDirectory, "valid.wld"),
                WorldGenerator.Generate(WorldSize.Small, "Valid", 123, crimson: true), keepBackup: false);
            File.WriteAllText(Path.Combine(manager.WorldsDirectory, "invalid.wld"), "not a world");

            var worlds = manager.ListWorlds();

            Assert.Contains(worlds, world => world.IsAvailable && world.Name == "Valid" && world.Width == 4200 && world.Seed == "123");
            Assert.Contains(worlds, world => !world.IsAvailable && world.Path.EndsWith("invalid.wld", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ImportWorld_ValidatesAndDoesNotOverwriteExistingWorld()
    {
        var directory = CreateDirectory();
        try
        {
            var manager = new WorldManager(Path.Combine(directory, "server.json"));
            var source = Path.Combine(directory, "source.wld");
            WorldFileWriter.Write(source, WorldGenerator.Generate(WorldSize.Small, "Import", 456), keepBackup: false);
            Directory.CreateDirectory(manager.WorldsDirectory);
            var existing = Path.Combine(manager.WorldsDirectory, "source.wld");
            File.WriteAllText(existing, "existing");

            var imported = manager.ImportWorld(source);

            Assert.NotEqual(existing, imported);
            Assert.Equal("existing", File.ReadAllText(existing));
            Assert.Equal("Import", WorldFileReader.Read(imported).WorldName);
            var config = ReadConfig(Path.Combine(directory, "server.json"));
            Assert.Equal(Path.GetFullPath(imported), config.WorldPath);
            Assert.Equal(config.WorldPath, config.WorldExportPath);
            Assert.True(config.ResetWorldChangesOnStart);

            var invalid = Path.Combine(directory, "invalid.wld");
            File.WriteAllText(invalid, "invalid");
            Assert.ThrowsAny<Exception>(() => manager.ImportWorld(invalid));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateWorld_SavesRequestedParametersAndPreservesConfiguration()
    {
        var directory = CreateDirectory();
        try
        {
            var configPath = Path.Combine(directory, "server.json");
            ConfigurationService.Write(configPath, new ServerConfig
            {
                MaxConnections = 12,
                MetricsEnabled = false,
                PlayerWhitelist = ["Ada"],
            });
            var manager = new WorldManager(configPath);

            var path = manager.CreateWorld("Created", WorldSize.Small, GameMode.Master, WorldEvil.Crimson, 789);

            var world = WorldFileReader.Read(path);
            Assert.Equal("Created", world.WorldName);
            Assert.Equal(4200, world.MaxTilesX);
            Assert.Equal(1200, world.MaxTilesY);
            Assert.Equal((int)GameMode.Master, world.GameMode);
            Assert.True(world.Progress.Crimson);
            Assert.Equal("789", world.Seed);

            var config = ReadConfig(configPath);
            Assert.Equal(12, config.MaxConnections);
            Assert.False(config.MetricsEnabled);
            Assert.Equal(["Ada"], config.PlayerWhitelist);
            Assert.Equal(Path.GetFullPath(path), config.WorldPath);
            Assert.Equal(config.WorldPath, config.WorldExportPath);
            Assert.True(config.ResetWorldChangesOnStart);
            Assert.Equal(GameMode.Master, config.GameMode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ConsoleMenu_ReturnsOnEndOfInput()
    {
        var directory = CreateDirectory();
        try
        {
            using var input = new StringReader(string.Empty);
            using var output = new StringWriter();

            WorldManagementConsole.Run(Path.Combine(directory, "server.json"), input, output);

            Assert.Contains("1. 选择已有世界", output.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ConsoleMenu_SelectsExistingWorldAndWritesConfiguration()
    {
        var directory = CreateDirectory();
        try
        {
            var configPath = Path.Combine(directory, "server.json");
            var manager = new WorldManager(configPath);
            Directory.CreateDirectory(manager.WorldsDirectory);
            var worldPath = Path.Combine(manager.WorldsDirectory, "selected.wld");
            WorldFileWriter.Write(worldPath, WorldGenerator.Generate(WorldSize.Small, "Selected", 11), keepBackup: false);

            using var input = new StringReader("1\n1\n");
            using var output = new StringWriter();
            WorldManagementConsole.Run(configPath, input, output);

            var config = ReadConfig(configPath);
            Assert.Equal(Path.GetFullPath(worldPath), config.WorldPath);
            Assert.Contains("Selected", output.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ConsoleMenu_CreatesWorldFromRequestedOptions()
    {
        var directory = CreateDirectory();
        try
        {
            var configPath = Path.Combine(directory, "server.json");
            using var input = new StringReader("2\nCreated\nSmall\nMaster\nCrimson\n12345\n");
            using var output = new StringWriter();
            WorldManagementConsole.Run(configPath, input, output);

            var config = ReadConfig(configPath);
            var world = WorldFileReader.Read(config.WorldPath);
            Assert.Equal("Created", world.WorldName);
            Assert.Equal((int)GameMode.Master, world.GameMode);
            Assert.True(world.Progress.Crimson);
            Assert.Equal("12345", world.Seed);
            Assert.Contains("已创建", output.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ConsoleMenu_ReportsImportFailureWithoutChangingConfiguration()
    {
        var directory = CreateDirectory();
        try
        {
            var configPath = Path.Combine(directory, "server.json");
            ConfigurationService.Write(configPath, new ServerConfig { WorldSeed = 77 });
            using var input = new StringReader("3\nmissing.wld\n");
            using var output = new StringWriter();
            WorldManagementConsole.Run(configPath, input, output);

            var config = ReadConfig(configPath);
            Assert.Equal("", config.WorldPath);
            Assert.Equal(77, config.WorldSeed);
            Assert.Contains("导入失败", output.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ServerConfig ReadConfig(string path) => JsonSerializer.Deserialize<ServerConfig>(
        File.ReadAllText(path), ConfigurationService.JsonOptions)!;

    private static string CreateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"terraauth-world-manager-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
