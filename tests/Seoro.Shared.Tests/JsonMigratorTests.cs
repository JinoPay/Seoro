using System.Text.Json;
using System.Text.Json.Nodes;
using Seoro.Shared.Models;
using Seoro.Shared.Services;
using Seoro.Shared.Services.Migration;

namespace Seoro.Shared.Tests;

public class SchemaMigrationTests
{
    private static readonly JsonSerializerOptions Options = JsonDefaults.Options;

    [Fact]
    public void MigratingJsonReader_NoVersionField_TreatedAsV1_MigratedToV2()
    {
        // 버전 필드 없는 파일은 v1로 간주 → v2로 마이그레이션 적용
        var json = """{"id": "abc", "name": "test"}""";

        var result = MigratingJsonReader.Read<Workspace>(json, Options);

        Assert.True(result.WasMigrated);
        Assert.NotNull(result.Result);
        Assert.Equal(int.MaxValue, result.Result!.SortIndex);
    }

    [Fact]
    public void MigratingJsonReader_AlreadyCurrentVersion_NoMigration()
    {
        var json = """{"$schemaVersion": 2, "id": "abc", "name": "test"}""";

        var result = MigratingJsonReader.Read<Workspace>(json, Options);

        Assert.False(result.WasMigrated);
    }

    [Fact]
    public void MigratingJsonReader_UnregisteredType_PlainDeserialize()
    {
        // 등록되지 않은 타입은 마이그레이션 없이 역직렬화
        var json = """{"id": "abc"}""";

        var result = MigratingJsonReader.Read<UnregisteredModel>(json, Options);

        Assert.False(result.WasMigrated);
        Assert.NotNull(result.Result);
    }

    [Fact]
    public void MigratingJsonWriter_InjectsSchemaVersion()
    {
        var workspace = new Workspace { Id = "test-id", Name = "Test" };

        var json = MigratingJsonWriter.Write(workspace, Options);

        var obj = JsonNode.Parse(json)!.AsObject();
        Assert.True(obj.ContainsKey(SchemaVersion.FieldName));
        Assert.Equal(2, obj[SchemaVersion.FieldName]!.GetValue<int>());
    }

    [Fact]
    public void SchemaMigrator_SequentialMigrations_Applied()
    {
        var migrations = new IJsonMigration[]
        {
            new TestMigration(1, 2, doc => { doc["addedInV2"] = "hello"; }),
            new TestMigration(2, 3, doc => { doc["addedInV3"] = 42; })
        };
        var migrator = new SchemaMigrator(3, migrations);

        var json = """{"$schemaVersion": 1, "name": "test"}""";

        var result = migrator.DeserializeAndMigrate<TestModel>(json, Options);

        Assert.True(result.WasMigrated);
        Assert.NotNull(result.MigratedJson);
        var obj = JsonNode.Parse(result.MigratedJson)!.AsObject();
        Assert.Equal(3, obj[SchemaVersion.FieldName]!.GetValue<int>());
        Assert.Equal("hello", obj["addedInV2"]!.GetValue<string>());
        Assert.Equal(42, obj["addedInV3"]!.GetValue<int>());
    }

    [Fact]
    public void SchemaMigrator_PartialMigration_OnlyAppliesNeeded()
    {
        var migrations = new IJsonMigration[]
        {
            new TestMigration(1, 2, doc => { doc["v2Field"] = "a"; }),
            new TestMigration(2, 3, doc => { doc["v3Field"] = "b"; })
        };
        var migrator = new SchemaMigrator(3, migrations);

        // 이미 v2 → v2→v3 마이그레이션만 적용
        var json = """{"$schemaVersion": 2, "name": "test"}""";

        var result = migrator.DeserializeAndMigrate<TestModel>(json, Options);

        Assert.True(result.WasMigrated);
        var obj = JsonNode.Parse(result.MigratedJson!)!.AsObject();
        Assert.Equal(3, obj[SchemaVersion.FieldName]!.GetValue<int>());
        Assert.Equal("b", obj["v3Field"]!.GetValue<string>());
        Assert.False(obj.ContainsKey("v2Field")); // v1→v2 migration was NOT applied
    }

    [Fact]
    public void SchemaMigrator_PreservesExistingProperties()
    {
        var migrator = new SchemaMigrator(2);
        var json = """{"$schemaVersion": 1, "name": "my workspace", "id": "abc-123"}""";

        var result = migrator.DeserializeAndMigrate<TestModel>(json, Options);

        Assert.NotNull(result.Result);
        Assert.Equal("my workspace", result.Result!.Name);
    }

    [Fact]
    public void SchemaMigratorRegistry_ReturnsRegisteredMigrators()
    {
        Assert.NotNull(SchemaMigratorRegistry.GetMigrator<Session>());
        Assert.NotNull(SchemaMigratorRegistry.GetMigrator<Workspace>());
        Assert.NotNull(SchemaMigratorRegistry.GetMigrator<AppSettings>());
        Assert.NotNull(SchemaMigratorRegistry.GetMigrator<MemoryEntry>());
        Assert.NotNull(SchemaMigratorRegistry.GetMigrator<TaskItem>());
    }

    [Fact]
    public void SchemaMigratorRegistry_WorkspaceVersionIs2()
    {
        var migrator = SchemaMigratorRegistry.GetMigrator<Workspace>();
        Assert.NotNull(migrator);
        Assert.Equal(2, migrator!.CurrentVersion);
    }

    [Fact]
    public void WorkspaceV1ToV2Migration_AddsSortIndex()
    {
        var json = """{"$schemaVersion": 1, "id": "ws-1", "name": "Test"}""";

        var result = MigratingJsonReader.Read<Workspace>(json, Options);

        Assert.True(result.WasMigrated);
        Assert.NotNull(result.Result);
        Assert.Equal(int.MaxValue, result.Result!.SortIndex);

        var obj = JsonNode.Parse(result.MigratedJson!)!.AsObject();
        Assert.Equal(2, obj[SchemaVersion.FieldName]!.GetValue<int>());
    }

    [Fact]
    public void SchemaMigratorRegistry_SessionVersionIs4()
    {
        // v4: git.lastPrUrl 필드가 도입됨 (사용자 수동 입력 PR 링크).
        var migrator = SchemaMigratorRegistry.GetMigrator<Session>();
        Assert.NotNull(migrator);
        Assert.Equal(4, migrator!.CurrentVersion);
    }

    [Fact]
    public void SessionV3ToV4Migration_AddsLastPrUrlAsNull()
    {
        var doc = new System.Text.Json.Nodes.JsonObject
        {
            ["$schemaVersion"] = 3,
            ["git"] = new System.Text.Json.Nodes.JsonObject
            {
                ["worktreePath"] = "/tmp/wt",
                ["branchName"] = "seoro/foo"
            }
        };
        var migration = new SessionV3ToV4Migration();
        var migrated = migration.Migrate(doc);
        Assert.True(migrated["git"] is System.Text.Json.Nodes.JsonObject);
        var git = (System.Text.Json.Nodes.JsonObject)migrated["git"]!;
        Assert.True(git.ContainsKey("lastPrUrl"));
        Assert.Null(git["lastPrUrl"]);
    }

    // Test helpers
    private class TestModel
    {
        public string Name { get; set; } = "";
    }

    private class UnregisteredModel
    {
        public string Id { get; set; } = "";
    }

    private class TestMigration : IJsonMigration
    {
        private readonly Action<JsonObject> _action;
        public int FromVersion { get; }
        public int ToVersion { get; }

        public TestMigration(int from, int to, Action<JsonObject> action)
        {
            FromVersion = from;
            ToVersion = to;
            _action = action;
        }

        public JsonObject Migrate(JsonObject document)
        {
            _action(document);
            return document;
        }
    }
}
