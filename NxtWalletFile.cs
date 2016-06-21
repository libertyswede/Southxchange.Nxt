using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Southxchange.Nxt
{
    /// <summary>
    /// Manages the wallet file for nxt. 
    /// Separator character within same line: |
    /// File structure is as follows:
    /// -----------------------------------------------------------------
    /// line 1: main account (not used for user deposits, but rather withdrawals)
    /// line 2-n: user deposit account(s)
    /// -----------------------------------------------------------------
    /// Account structure (all on same line): secret phrase|address|public key|last known block id
    /// All properties will be stored as strings, except for last known block id (ulong)
    /// -----------------------------------------------------------------
    /// Example of an unencrypted wallet file:
    /// -----------------------------------------------------------------
    /// serve ash page crush candle shook ceiling hero deserve bloody coat ocean|NXT-874B-DWJK-WSA3-ED28Z|7c2e0d683c96537aa4fcaf71d6f61608d86db654728eb4ddcfc01e08c159ea4c|17056787494759431766
    /// creature brave lady shore hook warrior excuse guilty world lightning glass puff|NXT-4XAL-GGAW-TZB5-FLJWZ|d5c96b293829f42cf84cdd48cbb0f0ca92c114aaa216823ab1e3af14e666de7e|2680262203532249785
    /// -----------------------------------------------------------------
    /// </summary>
    class NxtWalletFile
    {
        private readonly string filepath;
        private const string separator = "|";
        private readonly object lockObject = new object();

        public NxtWalletFile(string filepath)
        {
            this.filepath = filepath;
        }

        public bool FileExists()
        {
            return File.Exists(filepath);
        }

        public void InitNewFile(NxtAccount mainAccount)
        {
            if (FileExists())
            {
                return;
            }
            CreateNewWalletFile(mainAccount);
        }

        public NxtAccount GetMainAccount()
        {
            string mainAccountString;
            lock (lockObject)
            {
                using (var streamreader = new StreamReader(filepath))
                {
                    mainAccountString = streamreader.ReadLine();
                }
            }
            return ParseAccounts(mainAccountString).Single();
        }

        public List<NxtAccount> GetAllAccounts(bool includeMainAccount = true)
        {
            string walletFileContent;
            lock (lockObject)
            {
                using (var streamreader = new StreamReader(filepath))
                {
                    walletFileContent = streamreader.ReadToEnd();
                }
            }
            var accountList = ParseAccounts(walletFileContent);
            if (!includeMainAccount)
            {
                accountList.RemoveAt(0);
            }
            return accountList;
        }

        public void UpdateLastBlockIds(List<NxtAccount> accounts)
        {
            string walletFileContent;
            lock (lockObject)
            {
                using (var streamreader = new StreamReader(filepath))
                {
                    walletFileContent = streamreader.ReadToEnd();
                }
                var existingAccountList = ParseAccounts(walletFileContent);
                using (var streamwriter = new StreamWriter(filepath, false))
                {
                    foreach (var existingAccount in existingAccountList)
                    {
                        var newAccount = accounts.SingleOrDefault(a => a.Address == existingAccount.Address);
                        if (newAccount != null)
                        {
                            existingAccount.LastKnownBlockId = newAccount.LastKnownBlockId;
                        }
                        WriteAccountToFileStream(existingAccount, streamwriter);
                    }
                }
            }
        }

        public void AddAccountToFile(NxtAccount account)
        {
            lock (lockObject)
            {
                string[] arrLine = File.ReadAllLines(filepath);
                using (var streamwriter = new StreamWriter(filepath))
                {
                    foreach (var line in arrLine)
                    {
                        streamwriter.WriteLine(line);
                    }

                    WriteAccountToFileStream(account, streamwriter);
                }
            }
        }

        private static void WriteAccountToFileStream(NxtAccount account, StreamWriter streamwriter)
        {
            streamwriter.WriteLine(account.SecretPhrase + separator +
                                    account.Address + separator +
                                    account.PublicKey + separator +
                                    account.LastKnownBlockId);
        }

        private List<NxtAccount> ParseAccounts(string accountText)
        {
            var matches = Regex.Matches(accountText, @"^([\w ]*)\|([\w-]*)\|(\w*)\|(\d*)", RegexOptions.Multiline);
            var accountList = new List<NxtAccount>();
            foreach (Match match in matches)
            {
                var account = new NxtAccount
                {
                    SecretPhrase = match.Groups[1].ToString(),
                    Address = match.Groups[2].ToString(),
                    PublicKey = match.Groups[3].ToString(),
                    LastKnownBlockId = ulong.Parse(match.Groups[4].ToString())
                };
                accountList.Add(account);
            }
            return accountList;
        }

        private void CreateNewWalletFile(NxtAccount mainAccount)
        {
            lock (lockObject)
            {
                var stream = File.Create(filepath);
                stream.Close();
            }
            AddAccountToFile(mainAccount);
        }
    }
}
