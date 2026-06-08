using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using RenOff.Core;

namespace RenOff.Data;

public sealed class LocalSqliteStore
{
    private readonly string _dbPath;

    public LocalSqliteStore(string dbPath)
    {
        _dbPath = dbPath;
        var dir = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        Initialize();
    }

    public IReadOnlyList<RenOffItem> GetAll()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                Id,
                SortOrder,
                Type,
                Title,
                Body,
                IsDone,
                CreatedAt,
                UpdatedAt
            FROM Items
            WHERE IsDeleted = 0
            ORDER BY SortOrder ASC, UpdatedAt DESC;
            """;

        using var reader = command.ExecuteReader();
        var items = new List<RenOffItem>();

        while (reader.Read())
        {
            var id = Guid.Parse(reader.GetString(0));
            var sortOrder = reader.GetInt32(1);
            var type = (RenOffItemType)reader.GetInt32(2);
            var title = reader.GetString(3);
            var body = reader.IsDBNull(4) ? "" : reader.GetString(4);
            var isDone = reader.GetInt32(5) != 0;
            var createdAt = DateTimeOffset.Parse(reader.GetString(6));
            var updatedAt = DateTimeOffset.Parse(reader.GetString(7));

            items.Add(new RenOffItem
            {
                Id = id,
                SortOrder = sortOrder,
                Type = type,
                Title = title,
                Body = body,
                IsDone = isDone,
                CreatedAt = createdAt,
                UpdatedAt = updatedAt,
            });
        }

        return items;
    }

    public void Upsert(RenOffItem item)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Items (Id, SortOrder, Type, Title, Body, IsDone, IsDeleted, CreatedAt, UpdatedAt)
            VALUES ($id, $sortOrder, $type, $title, $body, $isDone, 0, $createdAt, $updatedAt)
            ON CONFLICT(Id) DO UPDATE SET
                SortOrder = excluded.SortOrder,
                Type = excluded.Type,
                Title = excluded.Title,
                Body = excluded.Body,
                IsDone = excluded.IsDone,
                IsDeleted = 0,
                UpdatedAt = excluded.UpdatedAt;
            """;

        command.Parameters.AddWithValue("$id", item.Id.ToString("D"));
        command.Parameters.AddWithValue("$sortOrder", item.SortOrder);
        command.Parameters.AddWithValue("$type", (int)item.Type);
        command.Parameters.AddWithValue("$title", item.Title ?? "");
        command.Parameters.AddWithValue("$body", item.Body ?? "");
        command.Parameters.AddWithValue("$isDone", item.IsDone ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", item.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", item.UpdatedAt.ToString("O"));

        command.ExecuteNonQuery();
    }

    public void UpdateSortOrders(IReadOnlyList<(Guid ItemId, int SortOrder)> updates)
    {
        if (updates.Count == 0) return;

        using var connection = OpenConnection();
        using var tx = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText =
            """
            UPDATE Items
            SET SortOrder = $sortOrder
            WHERE Id = $id;
            """;

        var idParam = command.CreateParameter();
        idParam.ParameterName = "$id";
        command.Parameters.Add(idParam);

        var sortParam = command.CreateParameter();
        sortParam.ParameterName = "$sortOrder";
        command.Parameters.Add(sortParam);

        foreach (var (itemId, sortOrder) in updates)
        {
            idParam.Value = itemId.ToString("D");
            sortParam.Value = sortOrder;
            command.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public void Delete(Guid id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Items
            SET IsDeleted = 1, UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public string? GetSetting(string key)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Value
            FROM Settings
            WHERE Key = $key
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$key", key);

        var result = command.ExecuteScalar();
        return result is null or DBNull ? null : (string)result;
    }

    public void SetSetting(string key, string value)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Settings (Key, Value)
            VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET
                Value = excluded.Value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value ?? "");
        command.ExecuteNonQuery();
    }

    public Reminder? GetReminderForItem(Guid itemId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                r.Id,
                r.ItemId,
                r.ScheduledAtUtc,
                r.SnoozedUntilUtc,
                r.Status,
                r.LastFiredAtUtc,
                r.CreatedAtUtc,
                r.UpdatedAtUtc
            FROM Reminders r
            JOIN Items i ON i.Id = r.ItemId
            WHERE
                i.IsDeleted = 0
                AND r.ItemId = $itemId
                AND r.Status <> $dismissed
            ORDER BY r.UpdatedAtUtc DESC
            LIMIT 1;
            """;

        command.Parameters.AddWithValue("$itemId", itemId.ToString("D"));
        command.Parameters.AddWithValue("$dismissed", (int)ReminderStatus.Dismissed);

        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;

        return ReadReminder(reader);
    }

    public void UpsertReminder(Reminder reminder)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Reminders (
                Id,
                ItemId,
                ScheduledAtUtc,
                SnoozedUntilUtc,
                Status,
                LastFiredAtUtc,
                CreatedAtUtc,
                UpdatedAtUtc
            )
            VALUES (
                $id,
                $itemId,
                $scheduledAtUtc,
                $snoozedUntilUtc,
                $status,
                $lastFiredAtUtc,
                $createdAtUtc,
                $updatedAtUtc
            )
            ON CONFLICT(Id) DO UPDATE SET
                ScheduledAtUtc = excluded.ScheduledAtUtc,
                SnoozedUntilUtc = excluded.SnoozedUntilUtc,
                Status = excluded.Status,
                LastFiredAtUtc = excluded.LastFiredAtUtc,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """;

        command.Parameters.AddWithValue("$id", reminder.Id.ToString("D"));
        command.Parameters.AddWithValue("$itemId", reminder.ItemId.ToString("D"));
        command.Parameters.AddWithValue("$scheduledAtUtc", reminder.ScheduledAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$snoozedUntilUtc", reminder.SnoozedUntilUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$status", (int)reminder.Status);
        command.Parameters.AddWithValue("$lastFiredAtUtc", reminder.LastFiredAtUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$createdAtUtc", reminder.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedAtUtc", reminder.UpdatedAtUtc.ToString("O"));

        command.ExecuteNonQuery();
    }

    public void SnoozeReminder(Guid reminderId, DateTimeOffset snoozedUntilUtc)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Reminders
            SET
                Status = $status,
                SnoozedUntilUtc = $snoozedUntilUtc,
                UpdatedAtUtc = $updatedAtUtc
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue("$id", reminderId.ToString("D"));
        command.Parameters.AddWithValue("$status", (int)ReminderStatus.Snoozed);
        command.Parameters.AddWithValue("$snoozedUntilUtc", snoozedUntilUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public void DismissReminder(Guid reminderId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Reminders
            SET
                Status = $status,
                UpdatedAtUtc = $updatedAtUtc
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue("$id", reminderId.ToString("D"));
        command.Parameters.AddWithValue("$status", (int)ReminderStatus.Dismissed);
        command.Parameters.AddWithValue("$updatedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<ReminderNotification> DequeueDueReminders(DateTimeOffset nowUtc, int limit = 3)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                r.Id,
                r.ItemId,
                i.Title,
                COALESCE(r.SnoozedUntilUtc, r.ScheduledAtUtc) AS EffectiveAtUtc
            FROM Reminders r
            JOIN Items i ON i.Id = r.ItemId
            WHERE
                i.IsDeleted = 0
                AND r.Status IN ($scheduled, $snoozed)
                AND COALESCE(r.SnoozedUntilUtc, r.ScheduledAtUtc) <= $nowUtc
            ORDER BY EffectiveAtUtc ASC
            LIMIT $limit;
            """;

        command.Parameters.AddWithValue("$scheduled", (int)ReminderStatus.Scheduled);
        command.Parameters.AddWithValue("$snoozed", (int)ReminderStatus.Snoozed);
        command.Parameters.AddWithValue("$nowUtc", nowUtc.ToString("O"));
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var list = new List<ReminderNotification>();
        var ids = new List<string>();

        while (reader.Read())
        {
            var reminderId = Guid.Parse(reader.GetString(0));
            var itemId = Guid.Parse(reader.GetString(1));
            var title = reader.GetString(2);
            var effectiveAt = DateTimeOffset.Parse(reader.GetString(3));

            list.Add(new ReminderNotification
            {
                ReminderId = reminderId,
                ItemId = itemId,
                ItemTitle = title,
                EffectiveAtUtc = effectiveAt,
            });
            ids.Add(reminderId.ToString("D"));
        }

        if (ids.Count > 0)
        {
            using var update = connection.CreateCommand();
            var paramNames = new List<string>(ids.Count);
            for (var i = 0; i < ids.Count; i++)
            {
                var name = $"$id{i}";
                paramNames.Add(name);
                update.Parameters.AddWithValue(name, ids[i]);
            }

            update.CommandText =
                $"""
                 UPDATE Reminders
                 SET
                     Status = $fired,
                     LastFiredAtUtc = $nowUtc,
                     UpdatedAtUtc = $nowUtc
                 WHERE Id IN ({string.Join(",", paramNames)});
                 """;

            update.Parameters.AddWithValue("$fired", (int)ReminderStatus.Fired);
            update.Parameters.AddWithValue("$nowUtc", nowUtc.ToString("O"));

            update.ExecuteNonQuery();
        }

        return list;
    }

    private SqliteConnection OpenConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        };

        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }

    private void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS Items (
                Id TEXT PRIMARY KEY,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                Type INTEGER NOT NULL,
                Title TEXT NOT NULL,
                Body TEXT NOT NULL,
                IsDone INTEGER NOT NULL,
                IsDeleted INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Reminders (
                Id TEXT PRIMARY KEY,
                ItemId TEXT NOT NULL,
                ScheduledAtUtc TEXT NOT NULL,
                SnoozedUntilUtc TEXT NULL,
                Status INTEGER NOT NULL,
                LastFiredAtUtc TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                FOREIGN KEY(ItemId) REFERENCES Items(Id)
            );

            CREATE TABLE IF NOT EXISTS Settings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            """;

        command.ExecuteNonQuery();

        EnsureItemsColumnExists(connection, columnName: "SortOrder", addSql: "ALTER TABLE Items ADD COLUMN SortOrder INTEGER NOT NULL DEFAULT 0;");
    }

    private static void EnsureItemsColumnExists(SqliteConnection connection, string columnName, string addSql)
    {
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA table_info(Items);";
        using var reader = pragma.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(1);
            if (name.Equals(columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = addSql;
        alter.ExecuteNonQuery();
    }

    private static Reminder ReadReminder(SqliteDataReader reader)
    {
        var id = Guid.Parse(reader.GetString(0));
        var itemId = Guid.Parse(reader.GetString(1));
        var scheduledAtUtc = DateTimeOffset.Parse(reader.GetString(2));
        var snoozedUntilUtc = reader.IsDBNull(3) ? (DateTimeOffset?)null : DateTimeOffset.Parse(reader.GetString(3));
        var status = (ReminderStatus)reader.GetInt32(4);
        var lastFiredAtUtc = reader.IsDBNull(5) ? (DateTimeOffset?)null : DateTimeOffset.Parse(reader.GetString(5));
        var createdAtUtc = DateTimeOffset.Parse(reader.GetString(6));
        var updatedAtUtc = DateTimeOffset.Parse(reader.GetString(7));

        return new Reminder
        {
            Id = id,
            ItemId = itemId,
            ScheduledAtUtc = scheduledAtUtc,
            SnoozedUntilUtc = snoozedUntilUtc,
            Status = status,
            LastFiredAtUtc = lastFiredAtUtc,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = updatedAtUtc,
        };
    }
}
