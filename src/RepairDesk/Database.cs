using Microsoft.Data.Sqlite;
using System.IO;
using System.Text.Json;

namespace RepairDesk;

public static class Database
{
    private static string DataFolder => StorageConfig.GetDataFolder();
    public static string DbPath => Path.Combine(DataFolder, "repairdesk.db");
    private static string ConnectionString => $"Data Source={DbPath}";

    public static void Initialize()
    {
        Directory.CreateDirectory(DataFolder);
        using var connection = Open();
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Brands (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL UNIQUE COLLATE NOCASE);
            CREATE TABLE IF NOT EXISTS PhoneModels (Id INTEGER PRIMARY KEY AUTOINCREMENT, BrandId INTEGER NOT NULL, Name TEXT NOT NULL COLLATE NOCASE, UNIQUE(BrandId, Name), FOREIGN KEY(BrandId) REFERENCES Brands(Id));
            CREATE TABLE IF NOT EXISTS Repairs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, PracticeNumber TEXT NOT NULL UNIQUE, CreatedAt TEXT NOT NULL,
                FirstName TEXT NOT NULL, LastName TEXT NOT NULL, Phone TEXT NOT NULL, Email TEXT,
                Brand TEXT NOT NULL, Model TEXT NOT NULL, Color TEXT, Imei TEXT, RepairDescription TEXT NOT NULL,
                RepairTypes TEXT NOT NULL, Accessories TEXT NOT NULL, DeviceConditions TEXT NOT NULL, ConditionNotes TEXT
            );
            CREATE TABLE IF NOT EXISTS Settings (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS InventoryParts (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, Code TEXT NOT NULL UNIQUE COLLATE NOCASE,
                Name TEXT NOT NULL, Category TEXT NOT NULL, Quantity INTEGER NOT NULL DEFAULT 0 CHECK(Quantity >= 0)
            );
            CREATE TABLE IF NOT EXISTS RepairParts (
                RepairId INTEGER NOT NULL, PartId INTEGER NOT NULL, Quantity INTEGER NOT NULL CHECK(Quantity > 0),
                PRIMARY KEY(RepairId, PartId), FOREIGN KEY(RepairId) REFERENCES Repairs(Id) ON DELETE CASCADE,
                FOREIGN KEY(PartId) REFERENCES InventoryParts(Id)
            );
            """;
        command.ExecuteNonQuery();
        EnsureColumn(connection, "Repairs", "AppointmentAt", "TEXT NULL");
        EnsureColumn(connection, "Repairs", "EmployeeCode", "TEXT NOT NULL DEFAULT ''");
        SeedCatalog(connection);
    }

    public static void SwitchStorage(StorageOptions newOptions)
    {
        var currentPath = DbPath;
        var targetFolder = StorageConfig.GetDataFolder(newOptions);
        var targetPath = Path.Combine(targetFolder, "repairdesk.db");
        Directory.CreateDirectory(targetFolder);
        if (!Path.GetFullPath(currentPath).Equals(Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase) && File.Exists(currentPath))
        {
            if (File.Exists(targetPath)) File.Copy(targetPath, targetPath + $".backup-{DateTime.Now:yyyyMMdd-HHmmss}", true);
            File.Copy(currentPath, targetPath, true);
        }
        StorageConfig.Save(newOptions);
        Initialize();
    }

    private static void EnsureColumn(SqliteConnection connection, string table, string column, string definition)
    {
        using var info = connection.CreateCommand();
        info.CommandText = $"PRAGMA table_info({table})";
        using var reader = info.ExecuteReader();
        while (reader.Read()) if (reader.GetString(1).Equals(column, StringComparison.OrdinalIgnoreCase)) return;
        reader.Close();
        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }

    private static SqliteConnection Open()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    private static void SeedCatalog(SqliteConnection connection)
    {
        using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM Brands";
        if (Convert.ToInt32(count.ExecuteScalar()) > 0) return;

        using var transaction = connection.BeginTransaction();
        foreach (var item in CatalogSeed.Data)
        {
            using var brand = connection.CreateCommand();
            brand.Transaction = transaction;
            brand.CommandText = "INSERT INTO Brands(Name) VALUES($name); SELECT last_insert_rowid();";
            brand.Parameters.AddWithValue("$name", item.Key);
            var brandId = Convert.ToInt64(brand.ExecuteScalar());
            foreach (var modelName in item.Value)
            {
                using var model = connection.CreateCommand();
                model.Transaction = transaction;
                model.CommandText = "INSERT INTO PhoneModels(BrandId, Name) VALUES($brandId, $name)";
                model.Parameters.AddWithValue("$brandId", brandId);
                model.Parameters.AddWithValue("$name", modelName);
                model.ExecuteNonQuery();
            }
        }
        transaction.Commit();
    }

    public static List<string> GetBrands()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name FROM Brands ORDER BY CASE WHEN Name='Altro' THEN 1 ELSE 0 END, Name";
        using var reader = command.ExecuteReader();
        var result = new List<string>();
        while (reader.Read()) result.Add(reader.GetString(0));
        return result;
    }

    public static List<string> GetModels(string brand)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT m.Name FROM PhoneModels m JOIN Brands b ON b.Id=m.BrandId WHERE b.Name=$brand ORDER BY m.Name";
        command.Parameters.AddWithValue("$brand", brand);
        using var reader = command.ExecuteReader();
        var result = new List<string>();
        while (reader.Read()) result.Add(reader.GetString(0));
        return result;
    }

    public static void AddBrand(string name)
    {
        name = name.Trim();
        if (name.Length == 0) return;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO Brands(Name) VALUES($name)";
        command.Parameters.AddWithValue("$name", name);
        command.ExecuteNonQuery();
    }

    public static void AddModel(string brand, string model)
    {
        AddBrand(brand);
        model = model.Trim();
        if (model.Length == 0) return;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO PhoneModels(BrandId,Name) SELECT Id,$model FROM Brands WHERE Name=$brand COLLATE NOCASE";
        command.Parameters.AddWithValue("$brand", brand.Trim());
        command.Parameters.AddWithValue("$model", model);
        command.ExecuteNonQuery();
    }

    public static int SaveRepair(RepairRecord item)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Repairs(PracticeNumber,CreatedAt,AppointmentAt,EmployeeCode,FirstName,LastName,Phone,Email,Brand,Model,Color,Imei,RepairDescription,RepairTypes,Accessories,DeviceConditions,ConditionNotes)
            VALUES($practice,$created,$appointment,$employee,$first,$last,$phone,$email,$brand,$model,$color,$imei,$description,$types,$accessories,$conditions,$notes);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$practice", item.PracticeNumber);
        command.Parameters.AddWithValue("$created", item.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$appointment", item.AppointmentAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$employee", item.EmployeeCode);
        command.Parameters.AddWithValue("$first", item.FirstName);
        command.Parameters.AddWithValue("$last", item.LastName);
        command.Parameters.AddWithValue("$phone", item.Phone);
        command.Parameters.AddWithValue("$email", item.Email);
        command.Parameters.AddWithValue("$brand", item.Brand);
        command.Parameters.AddWithValue("$model", item.Model);
        command.Parameters.AddWithValue("$color", item.Color);
        command.Parameters.AddWithValue("$imei", item.Imei);
        command.Parameters.AddWithValue("$description", item.RepairDescription);
        command.Parameters.AddWithValue("$types", JsonSerializer.Serialize(item.RepairTypes));
        command.Parameters.AddWithValue("$accessories", JsonSerializer.Serialize(item.Accessories));
        command.Parameters.AddWithValue("$conditions", JsonSerializer.Serialize(item.DeviceConditions));
        command.Parameters.AddWithValue("$notes", item.ConditionNotes);
        var id = Convert.ToInt32(command.ExecuteScalar());
        ApplyPartChanges(connection, transaction, id, [], item.UsedParts);
        transaction.Commit();
        return id;
    }

    public static void UpdateRepair(RepairRecord item)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var oldParts = GetRepairParts(connection, item.Id, transaction);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE Repairs SET AppointmentAt=$appointment,EmployeeCode=$employee,FirstName=$first,LastName=$last,Phone=$phone,Email=$email,Brand=$brand,Model=$model,
            Color=$color,Imei=$imei,RepairDescription=$description,RepairTypes=$types,Accessories=$accessories,DeviceConditions=$conditions,ConditionNotes=$notes
            WHERE Id=$id
            """;
        AddRepairParameters(command, item);
        command.Parameters.AddWithValue("$id", item.Id);
        command.ExecuteNonQuery();
        ApplyPartChanges(connection, transaction, item.Id, oldParts, item.UsedParts);
        transaction.Commit();
    }

    private static void AddRepairParameters(SqliteCommand command, RepairRecord item)
    {
        command.Parameters.AddWithValue("$appointment", item.AppointmentAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$employee", item.EmployeeCode);
        command.Parameters.AddWithValue("$first", item.FirstName); command.Parameters.AddWithValue("$last", item.LastName);
        command.Parameters.AddWithValue("$phone", item.Phone); command.Parameters.AddWithValue("$email", item.Email);
        command.Parameters.AddWithValue("$brand", item.Brand); command.Parameters.AddWithValue("$model", item.Model);
        command.Parameters.AddWithValue("$color", item.Color); command.Parameters.AddWithValue("$imei", item.Imei);
        command.Parameters.AddWithValue("$description", item.RepairDescription);
        command.Parameters.AddWithValue("$types", JsonSerializer.Serialize(item.RepairTypes));
        command.Parameters.AddWithValue("$accessories", JsonSerializer.Serialize(item.Accessories));
        command.Parameters.AddWithValue("$conditions", JsonSerializer.Serialize(item.DeviceConditions));
        command.Parameters.AddWithValue("$notes", item.ConditionNotes);
    }

    public static void UpdateAppointment(int id, DateTime? appointment)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Repairs SET AppointmentAt=$appointment WHERE Id=$id";
        command.Parameters.AddWithValue("$appointment", appointment?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$id", id); command.ExecuteNonQuery();
    }

    public static void DeleteRepair(int id)
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        var parts = GetRepairParts(connection, id, transaction);
        foreach (var part in parts)
        {
            using var restore = connection.CreateCommand(); restore.Transaction = transaction;
            restore.CommandText = "UPDATE InventoryParts SET Quantity=Quantity+$qty WHERE Id=$id";
            restore.Parameters.AddWithValue("$qty", part.Quantity); restore.Parameters.AddWithValue("$id", part.PartId); restore.ExecuteNonQuery();
        }
        using var links = connection.CreateCommand(); links.Transaction = transaction; links.CommandText = "DELETE FROM RepairParts WHERE RepairId=$id"; links.Parameters.AddWithValue("$id", id); links.ExecuteNonQuery();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "DELETE FROM Repairs WHERE Id=$id"; command.Parameters.AddWithValue("$id", id); command.ExecuteNonQuery();
        transaction.Commit();
    }

    public static List<RepairRecord> SearchRepairs(string search = "")
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id,PracticeNumber,CreatedAt,FirstName,LastName,Phone,Email,Brand,Model,Color,Imei,RepairDescription,RepairTypes,Accessories,DeviceConditions,ConditionNotes,AppointmentAt,EmployeeCode
            FROM Repairs WHERE $q='' OR PracticeNumber LIKE $like OR FirstName LIKE $like OR LastName LIKE $like OR Phone LIKE $like OR Email LIKE $like OR Imei LIKE $like
            ORDER BY Id DESC LIMIT 500
            """;
        command.Parameters.AddWithValue("$q", search.Trim());
        command.Parameters.AddWithValue("$like", $"%{search.Trim()}%");
        using var reader = command.ExecuteReader();
        var result = new List<RepairRecord>();
        while (reader.Read()) result.Add(ReadRepair(reader));
        reader.Close();
        foreach (var repair in result) repair.UsedParts = GetRepairParts(connection, repair.Id);
        return result;
    }

    private static RepairRecord ReadRepair(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(0), PracticeNumber = r.GetString(1), CreatedAt = DateTime.Parse(r.GetString(2)),
        FirstName = r.GetString(3), LastName = r.GetString(4), Phone = r.GetString(5), Email = r.GetString(6),
        Brand = r.GetString(7), Model = r.GetString(8), Color = r.GetString(9), Imei = r.GetString(10), RepairDescription = r.GetString(11),
        RepairTypes = JsonSerializer.Deserialize<List<string>>(r.GetString(12)) ?? [], Accessories = JsonSerializer.Deserialize<List<string>>(r.GetString(13)) ?? [],
        DeviceConditions = JsonSerializer.Deserialize<List<string>>(r.GetString(14)) ?? [], ConditionNotes = r.GetString(15),
        AppointmentAt = r.IsDBNull(16) ? null : DateTime.Parse(r.GetString(16)), EmployeeCode = r.IsDBNull(17) ? "" : r.GetString(17)
    };

    public static List<RepairRecord> GetAppointments(DateTime day) => SearchRepairs()
        .Where(x => x.AppointmentAt?.Date == day.Date).OrderBy(x => x.AppointmentAt).ToList();

    public static List<RepairRecord> GetAllAppointments() => SearchRepairs()
        .Where(x => x.AppointmentAt is not null).OrderBy(x => x.AppointmentAt).ToList();

    private static List<UsedPart> GetRepairParts(SqliteConnection connection, int repairId, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT p.Id,p.Code,p.Name,rp.Quantity FROM RepairParts rp JOIN InventoryParts p ON p.Id=rp.PartId WHERE rp.RepairId=$id ORDER BY p.Code";
        command.Parameters.AddWithValue("$id", repairId); using var reader = command.ExecuteReader(); var result = new List<UsedPart>();
        while (reader.Read()) result.Add(new UsedPart { PartId=reader.GetInt32(0), Code=reader.GetString(1), Name=reader.GetString(2), Quantity=reader.GetInt32(3) });
        return result;
    }

    private static void ApplyPartChanges(SqliteConnection connection, SqliteTransaction transaction, int repairId, List<UsedPart> oldParts, List<UsedPart> newParts)
    {
        var ids = oldParts.Select(x => x.PartId).Union(newParts.Select(x => x.PartId));
        foreach (var partId in ids)
        {
            var oldQty = oldParts.FirstOrDefault(x => x.PartId == partId)?.Quantity ?? 0;
            var newQty = newParts.FirstOrDefault(x => x.PartId == partId)?.Quantity ?? 0;
            var difference = newQty - oldQty;
            if (difference > 0)
            {
                using var stock = connection.CreateCommand(); stock.Transaction = transaction;
                stock.CommandText = "UPDATE InventoryParts SET Quantity=Quantity-$qty WHERE Id=$id AND Quantity >= $qty";
                stock.Parameters.AddWithValue("$qty", difference); stock.Parameters.AddWithValue("$id", partId);
                if (stock.ExecuteNonQuery() == 0) throw new InvalidOperationException("Giacenza insufficiente per uno dei ricambi selezionati.");
            }
            else if (difference < 0)
            {
                using var stock = connection.CreateCommand(); stock.Transaction = transaction;
                stock.CommandText = "UPDATE InventoryParts SET Quantity=Quantity+$qty WHERE Id=$id";
                stock.Parameters.AddWithValue("$qty", -difference); stock.Parameters.AddWithValue("$id", partId); stock.ExecuteNonQuery();
            }
        }
        using var clear = connection.CreateCommand(); clear.Transaction = transaction; clear.CommandText = "DELETE FROM RepairParts WHERE RepairId=$id"; clear.Parameters.AddWithValue("$id", repairId); clear.ExecuteNonQuery();
        foreach (var part in newParts)
        {
            using var link = connection.CreateCommand(); link.Transaction = transaction;
            link.CommandText = "INSERT INTO RepairParts(RepairId,PartId,Quantity) VALUES($repair,$part,$qty)";
            link.Parameters.AddWithValue("$repair", repairId); link.Parameters.AddWithValue("$part", part.PartId); link.Parameters.AddWithValue("$qty", part.Quantity); link.ExecuteNonQuery();
        }
    }

    public static List<InventoryItem> GetInventory(string search = "")
    {
        using var connection=Open(); using var command=connection.CreateCommand();
        command.CommandText="SELECT Id,Code,Name,Category,Quantity FROM InventoryParts WHERE $q='' OR Code LIKE $like OR Name LIKE $like OR Category LIKE $like ORDER BY Category,Name";
        command.Parameters.AddWithValue("$q",search.Trim()); command.Parameters.AddWithValue("$like",$"%{search.Trim()}%"); using var reader=command.ExecuteReader(); var result=new List<InventoryItem>();
        while(reader.Read()) result.Add(new InventoryItem{Id=reader.GetInt32(0),Code=reader.GetString(1),Name=reader.GetString(2),Category=reader.GetString(3),Quantity=reader.GetInt32(4)}); return result;
    }

    public static InventoryItem? FindPartByCode(string code) => GetInventory(code).FirstOrDefault(x => x.Code.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase));

    public static void SaveInventoryItem(InventoryItem item)
    {
        using var connection=Open(); using var command=connection.CreateCommand();
        command.CommandText=item.Id==0 ? "INSERT INTO InventoryParts(Code,Name,Category,Quantity) VALUES($code,$name,$category,$qty)" : "UPDATE InventoryParts SET Code=$code,Name=$name,Category=$category,Quantity=$qty WHERE Id=$id";
        command.Parameters.AddWithValue("$code",item.Code.Trim()); command.Parameters.AddWithValue("$name",item.Name.Trim()); command.Parameters.AddWithValue("$category",item.Category.Trim()); command.Parameters.AddWithValue("$qty",item.Quantity); command.Parameters.AddWithValue("$id",item.Id); command.ExecuteNonQuery();
    }

    public static void DeleteInventoryItem(int id)
    {
        using var connection=Open(); using var check=connection.CreateCommand(); check.CommandText="SELECT COUNT(*) FROM RepairParts WHERE PartId=$id"; check.Parameters.AddWithValue("$id",id);
        if(Convert.ToInt32(check.ExecuteScalar())>0) throw new InvalidOperationException("Questo ricambio è collegato a una riparazione e non può essere eliminato. Puoi impostare la quantità a zero.");
        using var command=connection.CreateCommand(); command.CommandText="DELETE FROM InventoryParts WHERE Id=$id"; command.Parameters.AddWithValue("$id",id); command.ExecuteNonQuery();
    }

    public static string NextPracticeNumber() => $"R-{DateTime.Now:yyyyMMdd-HHmmssfff}";

    public static ShopSettings LoadSettings()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Settings WHERE Key='shop'";
        var json = command.ExecuteScalar()?.ToString();
        return string.IsNullOrWhiteSpace(json) ? new ShopSettings() : JsonSerializer.Deserialize<ShopSettings>(json) ?? new ShopSettings();
    }

    public static void SaveSettings(ShopSettings settings)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Settings(Key,Value) VALUES('shop',$value) ON CONFLICT(Key) DO UPDATE SET Value=$value";
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(settings));
        command.ExecuteNonQuery();
    }
}
