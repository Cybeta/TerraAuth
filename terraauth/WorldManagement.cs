using System.Text.Json;
using TerraAuth.Config;
using TerraAuth.Simulation;

namespace TerraAuth;

public enum WorldEvil
{
    Corruption,
    Crimson,
}

public sealed record ManagedWorld(string Path, string Name, int Width, int Height, int GameMode, string Seed, bool IsAvailable, string? Error);

public sealed class WorldManager
{
    private readonly string _configPath;

    public WorldManager(string configPath)
    {
        _configPath = Path.GetFullPath(configPath);
    }

    public string WorldsDirectory => Path.Combine(Path.GetDirectoryName(_configPath)!, "worlds");

    public IReadOnlyList<ManagedWorld> ListWorlds()
    {
        Directory.CreateDirectory(WorldsDirectory);
        var worlds = new List<ManagedWorld>();
        foreach (var path in Directory.EnumerateFiles(WorldsDirectory, "*.wld", SearchOption.TopDirectoryOnly)
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var world = WorldFileReader.Read(path);
                worlds.Add(new ManagedWorld(Path.GetFullPath(path), world.WorldName, world.MaxTilesX, world.MaxTilesY,
                    world.GameMode, world.Seed, true, null));
            }
            catch (Exception ex)
            {
                worlds.Add(new ManagedWorld(Path.GetFullPath(path), Path.GetFileNameWithoutExtension(path), 0, 0, 0, "",
                    false, ex.Message));
            }
        }

        return worlds;
    }

    public string ImportWorld(string sourcePath)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        _ = WorldFileReader.Read(fullSourcePath);

        Directory.CreateDirectory(WorldsDirectory);
        var destination = GetAvailableWorldPath(Path.GetFileName(fullSourcePath));
        File.Copy(fullSourcePath, destination, overwrite: false);
        SelectWorld(destination);
        return destination;
    }

    public string CreateWorld(string name, WorldSize size, GameMode gameMode, WorldEvil evil, int seed)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("世界名不能为空", nameof(name));

        var world = WorldGenerator.Generate(size, name.Trim(), seed, crimson: evil == WorldEvil.Crimson);
        world.GameMode = (int)gameMode;
        world.Progress.Crimson = evil == WorldEvil.Crimson;

        Directory.CreateDirectory(WorldsDirectory);
        var destination = GetAvailableWorldPath(ToFileName(name) + ".wld");
        WorldFileWriter.Write(destination, world, keepBackup: false);
        UpdateConfiguration(destination, gameMode);
        return destination;
    }

    public void SelectWorld(string worldPath)
    {
        var fullWorldPath = Path.GetFullPath(worldPath);
        _ = WorldFileReader.Read(fullWorldPath);
        UpdateConfiguration(fullWorldPath, null);
    }

    private void UpdateConfiguration(string worldPath, GameMode? gameMode)
    {
        var config = LoadConfiguration();
        var updated = config with
        {
            WorldPath = worldPath,
            WorldExportPath = worldPath,
            ResetWorldChangesOnStart = true,
            GameMode = gameMode ?? config.GameMode,
        };
        ConfigurationService.Write(_configPath, updated);
    }

    private ServerConfig LoadConfiguration()
    {
        if (!File.Exists(_configPath))
            return new ServerConfig();

        var json = File.ReadAllText(_configPath);
        return JsonSerializer.Deserialize<ServerConfig>(json, ConfigurationService.JsonOptions)
               ?? throw new InvalidDataException("配置解析失败");
    }

    private string GetAvailableWorldPath(string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var candidate = Path.Combine(WorldsDirectory, fileName);
        for (var index = 1; File.Exists(candidate); index++)
            candidate = Path.Combine(WorldsDirectory, $"{baseName} ({index}){extension}");
        return candidate;
    }

    private static string ToFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(result) ? "World" : result;
    }
}

internal static class WorldManagementConsole
{
    private static readonly HashSet<string> UnsupportedSpecialSeeds = new(StringComparer.OrdinalIgnoreCase)
    {
        "05162020", "5162020", "for the worthy", "not the bees", "celebrationmk10", "the constant", "no traps", "dont dig up", "get fixed boi",
    };

    public static void Run(string configPath, TextReader input, TextWriter output)
    {
        var manager = new WorldManager(configPath);
        while (true)
        {
            output.WriteLine("[World] 1. 选择已有世界  2. 创建世界  3. 导入 .wld  4. 直接使用当前配置世界");
            output.Write("> ");
            var choice = input.ReadLine();
            if (choice is null)
                return;

            switch (choice.Trim())
            {
                case "1":
                    ChooseWorld(manager, input, output);
                    return;
                case "2":
                    CreateWorld(manager, input, output);
                    return;
                case "3":
                    ImportWorld(manager, input, output);
                    return;
                case "4":
                    return;
                default:
                    output.WriteLine("请输入 1 至 4。");
                    break;
            }
        }
    }

    private static void ChooseWorld(WorldManager manager, TextReader input, TextWriter output)
    {
        var worlds = manager.ListWorlds().Where(world => world.IsAvailable).ToList();
        if (worlds.Count == 0)
        {
            output.WriteLine("[World] worlds 目录中没有可用世界。");
            return;
        }

        for (var i = 0; i < worlds.Count; i++)
        {
            var world = worlds[i];
            output.WriteLine($"{i + 1}. {world.Name} ({world.Width}x{world.Height}, 难度 {world.GameMode}, 种子 {world.Seed})");
        }
        output.Write("> ");
        if (!int.TryParse(input.ReadLine(), out var selected) || selected < 1 || selected > worlds.Count)
        {
            output.WriteLine("[World] 选择无效。");
            return;
        }

        manager.SelectWorld(worlds[selected - 1].Path);
    }

    private static void ImportWorld(WorldManager manager, TextReader input, TextWriter output)
    {
        output.Write(".wld 路径: ");
        var path = input.ReadLine();
        if (string.IsNullOrWhiteSpace(path))
        {
            output.WriteLine("[World] 路径不能为空。");
            return;
        }

        try
        {
            var imported = manager.ImportWorld(path);
            output.WriteLine($"[World] 已导入 {imported}");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            output.WriteLine($"[World] 导入失败：{ex.Message}");
        }
    }

    private static void CreateWorld(WorldManager manager, TextReader input, TextWriter output)
    {
        output.Write("世界名: ");
        var name = input.ReadLine() ?? "";
        output.Write("大小 (Small/Medium/Large): ");
        if (!Enum.TryParse<WorldSize>(input.ReadLine(), true, out var size))
        {
            output.WriteLine("[World] 大小无效。");
            return;
        }
        output.Write("难度 (Classic/Expert/Master): ");
        if (!Enum.TryParse<GameMode>(input.ReadLine(), true, out var gameMode))
        {
            output.WriteLine("[World] 难度无效。");
            return;
        }
        output.Write("邪恶类型 (Corruption/Crimson): ");
        if (!Enum.TryParse<WorldEvil>(input.ReadLine(), true, out var evil))
        {
            output.WriteLine("[World] 邪恶类型无效。");
            return;
        }
        output.Write("种子（空则随机；特殊世界种子尚未支持）: ");
        var seedText = input.ReadLine()?.Trim() ?? "";
        var seed = 0;
        if (UnsupportedSpecialSeeds.Contains(seedText))
        {
            output.WriteLine("[World] 特殊世界种子尚未支持。");
            return;
        }
        if (!string.IsNullOrEmpty(seedText))
        {
            if (!int.TryParse(seedText, out seed))
            {
                output.WriteLine("[World] 特殊世界种子尚未支持；普通种子必须是整数。");
                return;
            }
        }
        else
        {
            seed = Random.Shared.Next();
        }

        try
        {
            var path = manager.CreateWorld(name, size, gameMode, evil, seed);
            output.WriteLine($"[World] 已创建 {path}");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
        {
            output.WriteLine($"[World] 创建失败：{ex.Message}");
        }
    }
}
