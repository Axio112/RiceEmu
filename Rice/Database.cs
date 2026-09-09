using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.Entity;
using System.Data.SqlServerCe;
using System.IO;
using Rice.Server.Database.Models;

namespace Rice
{
    public static class DatabaseExtensions
    {
        public static DbCommand CreateTextCommand(this DbConnection dbconn, string query)
        {
            DbCommand command = dbconn.CreateCommand();
            command.CommandType = CommandType.Text;
            command.CommandText = query;
            return command;
        }

        public static void AddParameter(this DbCommand command, string name, object value)
        {
            DbParameter param = command.CreateParameter();
            param.ParameterName = name;
            param.Value = value;
            command.Parameters.Add(param);
        }
    }
    
    public class RiceContext : DbContext
    {
        public virtual DbSet<User> Users { get; set; }
        public virtual DbSet<Character> Characters { get; set; }
        public virtual DbSet<QuestState> QuestStates { get; set; }
        public virtual DbSet<Item> Items { get; set; }
        public virtual DbSet<Vehicle> Vehicles { get; set; }

        public RiceContext(DbConnection connection, bool ownsConnection = false) : base(connection, ownsConnection) { }
    }

    public static class Database
    {
        static DbConnection conn;

        public static void Initialize(Config config)
        {
            // The schema is maintained by hand against the shipped db.sdf, so no
            // initializer should try to verify or recreate it.
            System.Data.Entity.Database.SetInitializer<RiceContext>(null);

            string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "db.sdf");
            conn = new SqlCeConnection($"Data Source={dbPath}");
        }

        public static void Start()
        {
            Log.WriteLine("Connecting to database.");

            try
            {
                using (var rc = new RiceContext(conn))
                {
                    if (rc.Database.CreateIfNotExists())
                    {
                        Log.WriteLine("Database did not exist, created.");
                        
                        // Create some test data to work with
                        var testUser = new User
                        {
                            Name = "admin",
                            PasswordHash = Utilities.MD5("admin"),
                            CreateIP = "127.0.0.1",
                            Status = 1,
                            Credits = 1000
                        };

                        rc.Users.Add(testUser);
                        rc.SaveChanges();

                        // No navigation-property cascade: Character/Vehicle only carry
                        // plain UID/CID columns now, set by hand once the parent row's
                        // generated ID is known.
                        var testChar = new Character
                        {
                            UID = testUser.ID,
                            Name = "RiceAdmin",
                            Mito = 12345678910,
                            Avatar = 2,
                            Level = 123,
                            Experience = 123,
                            City = 1,
                            TID = -1
                        };
                        rc.Characters.Add(testChar);
                        rc.SaveChanges();

                        var testVehicle = new Vehicle
                        {
                            CID = testChar.ID,
                            CarID = 1,
                            AuctionCount = 0,
                            CarType = 88,
                            Color = 0,
                            Grade = 9,
                            Kms = 200,
                            Mitron = 5550f
                        };
                        rc.Vehicles.Add(testVehicle);

                        testChar.CurrentCarID = testVehicle.CarID;
                        rc.SaveChanges();
                    }
                }
                conn.Open();
                Log.WriteLine("Connected to database.");
            }
            catch (SqlCeException ex)
            {
                Log.WriteError($"Connection failed: {ex.Message}");
            }
        }

        private static DbConnection GetConnection()
        {
            if (conn.State == ConnectionState.Broken)
                conn.Close();
            if (conn.State == ConnectionState.Closed)
                Start();
            return conn;
        }

        public static RiceContext GetContext() => new RiceContext(GetConnection());
    }
}
