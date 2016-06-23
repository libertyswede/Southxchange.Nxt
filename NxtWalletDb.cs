using System;
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

        public void InitNewDb(NxtAccount mainAccount, ulong lastBlockId)
        {
            if (FileExists())
            {
                return;
            }
            CreateNewWalletFile(mainAccount, lastBlockId);
        }

        public NxtAccount GetMainAccount()
        {
            var sql = "SELECT id, secret_phrase, address, main_account FROM account WHERE main_account = 1";
            using (var dbConnection = OpenNewDbConnection())
            using (var command = new SQLiteCommand(sql, dbConnection))
            using (var reader = command.ExecuteReader())
            {
                reader.Read();
                var account = ParseAccount(reader);
                return account;
            }
        }

        public string GetSecretPhrase(long accountId)
        {
            var sql = $"SELECT secret_phrase FROM account WHERE id = {accountId}";
            using (var dbConnection = OpenNewDbConnection())
            using (var command = new SQLiteCommand(sql, dbConnection))
            {
                var secretPhrase = command.ExecuteScalar().ToString();
                return secretPhrase;
            }
        }

        public ulong GetLastBlockId()
        {
            using (var dbConnection = OpenNewDbConnection())
            {
                return GetLastBlockId(dbConnection);
            }
        }

        private ulong GetLastBlockId(SQLiteConnection dbConnection)
        {
            var sql = $"SELECT last_id FROM block";
            using (var command = new SQLiteCommand(sql, dbConnection))
            {
                var lastBlockId = (ulong)(long)command.ExecuteScalar();
                return lastBlockId;
            }
        }

        public List<NxtAddress> GetAllDepositAddresses()
        {
            const string sql = "SELECT id, address FROM account WHERE main_account = 0";
            var addresses = new List<NxtAddress>();

            using (var dbConnection = OpenNewDbConnection())
            using (var command = new SQLiteCommand(sql, dbConnection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var address = new NxtAddress
                    {
                        Id = (long)reader["id"],
                        Address = reader["address"].ToString()
                    };
                    addresses.Add(address);
                }
            }
            return addresses;
        }

        public List<NxtAccount> GetAllDepositAccounts()
        {
            var accounts = new List<NxtAccount>();
            var sql = "SELECT id, secret_phrase, address, main_account FROM account WHERE main_account = 0";
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

        public void UpdateLastBlockId(ulong lastBlockId)
        {
            using (var dbConnection = OpenNewDbConnection())
            {
                var sql = $"UPDATE block SET last_id = {(long)lastBlockId}";
                using (var command = new SQLiteCommand(sql, dbConnection))
                {
                    command.ExecuteNonQuery();
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

        public bool EncryptionKeyEquals(string keyToTest)
        {
            return string.Equals(encryptionKey, keyToTest);
        }

        public bool IsEncrypted()
        {
            try
            {
                using (var dbConnection = OpenNewDbConnection(""))
                {
                    var lastBlockId = GetLastBlockId(dbConnection);
                }
            }
            catch (SQLiteException e)
            {
                if (e.ResultCode == SQLiteErrorCode.NotADb)
                {
                    return true;
                }
            }
            return false;
        }

        public void ChangeKey(string key, string newKey)
        {
            if (!string.Equals(key, encryptionKey) && (!string.IsNullOrEmpty(key) || !string.IsNullOrEmpty(encryptionKey)))
            {
                throw new ArgumentException("Wrong key", nameof(key));
            }
            using (var dbConnection = OpenNewDbConnection())
            {
                dbConnection.ChangePassword(newKey);
                encryptionKey = newKey;
            }
        }

        private SQLiteConnection OpenNewDbConnection()
        {
            return OpenNewDbConnection(encryptionKey);
        }

        private SQLiteConnection OpenNewDbConnection(string key)
        {
            var dbConnection = new SQLiteConnection($"Data Source={filepath};Version=3;");
            dbConnection.SetPassword(key);
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
                Address = reader["address"].ToString()
            };
            return account;
        }

        private void CreateNewWalletFile(NxtAccount mainAccount, ulong lastBlockId)
        {

            using (var dbConnection = OpenNewDbConnection())
            {
                const string createAccountSql = "CREATE TABLE account (id INTEGER PRIMARY KEY, secret_phrase TEXT, address TEXT, main_account INTEGER)";
                using (var command = new SQLiteCommand(createAccountSql, dbConnection))
                {
                    command.ExecuteNonQuery();
                    AddAccount(mainAccount, dbConnection);
                }

                const string createBlockSql = "CREATE TABLE block (last_id INTEGER)";
                using (var command = new SQLiteCommand(createBlockSql, dbConnection))
                {
                    command.ExecuteNonQuery();
                }
                var insertBlockSql = $"INSERT INTO block (last_id) VALUES ({(long)lastBlockId})";
                using (var command = new SQLiteCommand(insertBlockSql, dbConnection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        private void AddAccount(NxtAccount account, SQLiteConnection dbConnection)
        {
            var isMainAccount = account.IsMainAccount ? "1" : "0";
            var sql = $"INSERT INTO account (secret_phrase, address, main_account) VALUES ('{account.SecretPhrase}', '{account.Address}', {isMainAccount})";
            using (var command = new SQLiteCommand(sql, dbConnection))
            {
                command.ExecuteNonQuery();
            }   

            account.Id = dbConnection.LastInsertRowId;
        }
    }
}
