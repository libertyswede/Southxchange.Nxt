using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

namespace Southxchange.Nxt
{
    /// <summary>
    /// This represents a NXT wallet file.
    /// The wallet is a Sqlite database which do support 128 bit AES encryption.
    /// </summary>
    class NxtWalletDb
    {
        private readonly string filepath;
        private string encryptionKey;

        public NxtWalletDb(string filepath, string encryptionKey)
        {
            this.filepath = filepath;
            this.encryptionKey = encryptionKey;
        }

        public bool FileExists()
        {
            return File.Exists(filepath);
        }

        public void InitNewDb(NxtAccount mainAccount)
        {
            if (FileExists())
            {
                return;
            }
            CreateNewWalletFile(mainAccount);
        }

        public NxtAccount GetMainAccount()
        {
            var sql = "SELECT id, secret_phrase, address, public_key, last_block_id, main_account FROM account WHERE main_account = 1";
            using (var dbConnection = OpenNewDbConnection())
            using (var command = new SQLiteCommand(sql, dbConnection))
            using (var reader = command.ExecuteReader())
            {
                reader.Read();
                var account = ParseAccount(reader);
                return account;
            }
        }

        public List<NxtAccount> GetAllDepositAccounts()
        {
            var accounts = new List<NxtAccount>();
            var sql = "SELECT id, secret_phrase, address, public_key, last_block_id, main_account FROM account WHERE main_account = 0";
            using (var dbConnection = OpenNewDbConnection())
            using (var command = new SQLiteCommand(sql, dbConnection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var account = ParseAccount(reader);
                    accounts.Add(account);
                }
            }
            return accounts;
        }

        public void UpdateLastBlockIds(List<NxtAccount> accounts)
        {
            using (var dbConnection = OpenNewDbConnection())
            {
                foreach (var account in accounts)
                {
                    var sql = $"UPDATE account SET last_block_id = {(long)account.LastKnownBlockId} WHERE id = {account.Id}";
                    using (var command = new SQLiteCommand(sql, dbConnection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
        }

        public void AddAccount(NxtAccount account)
        {
            using (var dbConnection = OpenNewDbConnection())
            {
                AddAccount(account, dbConnection);
            }
        }

        private SQLiteConnection OpenNewDbConnection()
        {
            var dbConnection = new SQLiteConnection($"Data Source={filepath};Version=3;Password={encryptionKey};");
            dbConnection.Open();
            return dbConnection;
        }

        private static NxtAccount ParseAccount(SQLiteDataReader reader)
        {
            var account = new NxtAccount
            {
                Id = (long)reader["id"],
                IsMainAccount = (long)reader["main_account"] == 1,
                SecretPhrase = reader["secret_phrase"].ToString(),
                Address = reader["address"].ToString(),
                PublicKey = reader["public_key"].ToString(),
                LastKnownBlockId = (ulong)(long)reader["last_block_id"]
            };
            return account;
        }

        private void CreateNewWalletFile(NxtAccount mainAccount)
        {
            const string sql = "CREATE TABLE account (id INTEGER PRIMARY KEY, secret_phrase TEXT, address TEXT, public_key TEXT, last_block_id INTEGER, main_account INTEGER)";

            using (var dbConnection = OpenNewDbConnection())
            using (var command = new SQLiteCommand(sql, dbConnection))
            {
                command.ExecuteNonQuery();
                AddAccount(mainAccount, dbConnection);
            }
        }

        private void AddAccount(NxtAccount account, SQLiteConnection dbConnection)
        {
            var isMainAccount = account.IsMainAccount ? "1" : "0";
            var sql = $"INSERT INTO account (secret_phrase, address, public_key, last_block_id, main_account) VALUES ('{account.SecretPhrase}', '{account.Address}', '{account.PublicKey}', {(long)account.LastKnownBlockId}, {isMainAccount})";
            using (var command = new SQLiteCommand(sql, dbConnection))
            {
                command.ExecuteNonQuery();
            }   

            account.Id = dbConnection.LastInsertRowId;
        }
    }
}
