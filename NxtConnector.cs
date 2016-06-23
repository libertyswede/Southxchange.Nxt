using System;
using System.Collections.Generic;
using Market.CurrencyManager.RpcCoin.Connectors;
using NxtLib.Local;
using NxtLib.Accounts;
using NxtLib.Networking;
using NxtLib.Transactions;
using System.Threading;
using NxtLib.Blocks;
using System.Linq;

namespace Southxchange.Nxt
{
    /// <summary>
    /// NXT does not have a wallet file, like bitcoin and bitcoin clones, in fact, it treats it's accounts rather differently.
    /// For example, in NXT you cannot use multiple inputs from multiple addresses to send to one (or more) output in one single transaction.
    /// Also, in NXT there's something called phased transactions, which means a transaction can be accepted and confirmed, but not yet executed. 
    /// Ie, the actual money transfer has been deferred in the future or is awaiting approval.
    /// These (and other) differences between NXT and bitcoin makes it tricky to squeeze into one and the same interface (IConnector).
    /// I've done my best to implement this in a way that will "mimic" bitcoin behavior, however, there will be differences that need to be discussed.
    /// 
    /// External libraries:
    /// I'm using an external library called NxtLib, which I have developed.
    /// https://github.com/libertyswede/NxtLib 
    /// This library takes care of the HTTP communication with the NXT server, JSON parsing and other helper utilities.
    /// For simplicity purposes I'm downloading a precompiled dll of NxtLib from nuget, however you should be  examening and using the source before 
    /// using this in a production environmnent.
    /// 
    /// About this implementation:
    /// * GetTransactionFees will return fees for the requested transaction, but there is also a deposit fee which is currently not communicated through
    ///   the IConnector interface. As soon as a new deposit is found, when calling ListTransactions(), that account is drained on all funds and sent
    ///   to what I call a "main account". This main account is used for withdrawals, and is not to be confused with regular user deposit accounts.
    /// 
    /// Limitations of this implementation:
    /// * This implementation is not safe for multithreading, if multiple threads/tasks call these methods it could lead to deadlocks in the wallet 
    ///   database and/or other unexpected problems.
    /// * Phased transactions is not yet supported, they are rather uncommon and must for now, be handled manually.
    /// * Unconfirmed transactions is not supported, however, blocktimes are 60 seconds in NXT and rarely go above 90 seconds.
    ///   So this is not as severe as it would be with bitcoin.
    /// * Exception handling is needed, what if the NXT server becomes unreachable? As it is now, most exceptions are thrown to calling method.
    /// * Not tested for performance with many addresses (large wallet file) nor heavy load.
    /// </summary>
    class NxtConnector : IConnector
    {
        private readonly NxtWalletDb walletDb;
        private readonly NxtLib.ServiceFactory serviceFactory;
        private readonly NxtLib.Amount Fee = NxtLib.Amount.OneNxt;
        private readonly NxtAccount mainAccount;
        private readonly List<NxtAddress> depositAddresses;
        private ulong lastBlockId;

        private Action<string> logger;
        
        /// <summary>
        /// Constructor for NxtConnector
        /// </summary>
        /// <param name="walletFilePath">The wallet file path</param>
        /// <param name="serverAddress">Address to the nxt http api</param>
        /// <param name="walletKey">The key required to unlock the wallet file</param>
        public NxtConnector(string walletFilePath, string walletKey, string serverAddress = Constants.DefaultNxtUrl)
        {
            serviceFactory = new NxtLib.ServiceFactory(serverAddress);
            walletDb = new NxtWalletDb(walletFilePath, walletKey);
            InitWalletFile();
            mainAccount = walletDb.GetMainAccount();
            lastBlockId = walletDb.GetLastBlockId();
            depositAddresses = walletDb.GetAllDepositAddresses();
        }

        /// <summary>
        /// Sets the delegate in charge of logging
        /// </summary>
        /// <param name="logger">Delegate that logs a string</param>
        public void SetLogger(Action<string> logger)
        {
            this.logger = logger;
        }

        /// <summary>
        /// Generates a new deposit address
        /// </summary>
        /// <returns>New deposit address</returns>
        public string GenerateAddress()
        {
            logger.Invoke("Starting to generate new address");

            var account = GenerateNewAccount();
            walletDb.AddAccount(account);
            depositAddresses.Add(account);

            logger.Invoke($"Done with generating new address, {account.Address}");
            return account.Address;
        }

        /// <summary>
        /// Returns some information regarding the wallet
        /// </summary>
        /// <returns>Wallet info</returns>
        public Info GetInfo()
        {
            logger.Invoke("Starting to get info");
            var networkingService = serviceFactory.CreateNetworkingService();
            var serverInfoService = serviceFactory.CreateServerInfoService();
            var accountService = serviceFactory.CreateAccountService();

            var mainAccountBalance = accountService.GetBalance(mainAccount.Address).Result.UnconfirmedBalance.Nxt;
            var getPeersReply = networkingService.GetPeers(PeersLocator.ByState(PeerInfo.PeerState.Connected)).Result;
            var blockchainStatus = serverInfoService.GetBlockchainStatus().Result;

            var info = new Info
            {
                Connections = getPeersReply.PeerList.Count,
                LastBlock = blockchainStatus.NumberOfBlocks - 1, // equals "height" in NXT, which is the usual measure
                Reserves = mainAccountBalance,
                Version = blockchainStatus.Version
            };

            logger.Invoke("Done with get info");
            return info;
        }

        /// <summary>
        /// Returns the fees consumed by a given transaction ID. It is always called for a transaction
        /// generated by this wallet (e.g. previously returned by SendTo)
        /// </summary>
        /// <param name="txId">Transaction ID</param>
        /// <returns>Fees</returns>
        public decimal GetTransactionFees(string txId)
        {
            logger.Invoke($"Starting to get transaction fees for txId: {txId}");
            var transactionId = 0UL;
            if (!ulong.TryParse(txId, out transactionId))
            {
                logger.Invoke($"Unable to parse {txId} to unsigned long value!");
                throw new ArgumentException("Argument must be a valid unsigned long value.", nameof(txId));
            }

            var transactionService = serviceFactory.CreateTransactionService();
            var transaction = transactionService.GetTransaction(GetTransactionLocator.ByTransactionId(transactionId)).Result;
            logger.Invoke($"Done with getting transaction fees for {txId} : {transaction.Fee.Nxt} NXT");
            return transaction.Fee.Nxt;
        }

        /// <summary>
        /// Verifies if a given string is a valid address
        /// </summary>
        /// <param name="address">Address to check</param>
        /// <returns>True if address is valid, False otherwise</returns>
        public bool IsAddressValid(string address)
        {
            logger.Invoke($"Starting to check if address \"{address}\" is valid");
            var utilService = serviceFactory.CreateUtilService();
            try
            {
                var result = utilService.RsConvert(address).Result;
                logger.Invoke($"Done with checking address, \"{address}\" is valid");
                return true;
            }
            catch (AggregateException ae)
            {
                ae.Handle(e => e is NxtLib.NxtException && e.Message == "Incorrect \"account\"");
            }
            logger.Invoke($"Done with checking address, \"{address}\" is invalid");
            return false;
        }

        /// <summary>
        /// Returns when the wallet is reachable and working properly
        /// </summary>
        public void Ping()
        {
            logger.Invoke($"Starting to ping NXT server at {DateTime.Now}");
            var serverInfoService = serviceFactory.CreateServerInfoService();
            bool isOffline = true;
            while (isOffline)
            {
                try
                {
                    var state = serverInfoService.GetState().Result;
                    isOffline = state.IsOffline;
                }
                catch (Exception)
                {
                    // Ignore
                }
                if (isOffline)
                {
                    Thread.Sleep(1000);
                }
            }
            logger.Invoke($"Done with ping:ing NXT server at {DateTime.Now}");
        }

        /// <summary>
        /// Returns a list of transactions. It must return pending and confirmed transactions, but confirmed transactions
        /// are expected to be returned only once. Pending transactions can be returned in multiple invocations until they
        /// become confirmed. This method requires internal state keeping to achieve this functionality.
        /// </summary>
        /// <returns></returns>
        public List<Transaction> ListTransactions()
        {
            logger.Invoke($"Starting to list transactions at {DateTime.Now}");
            var blockService = serviceFactory.CreateBlockService();
            var transactionService = serviceFactory.CreateTransactionService();
            var serverInfoService = serviceFactory.CreateServerInfoService();
            var transactions = new List<Transaction>();
            var blockTimes = new Dictionary<ulong, DateTime>();
            logger.Invoke($"Will check {depositAddresses.Count} accounts for new transactions");

            var addressesSet = new HashSet<string>(depositAddresses.Select(a => a.Address));
            var height = blockService.GetBlock(BlockLocator.ByBlockId(lastBlockId)).Result.Height;
            var previousBlockId = lastBlockId;
            var hasMoreBlocks = true;
            while (hasMoreBlocks)
            {
                height++;
                NxtLib.Block<NxtLib.Transaction> block = null;
                try
                {
                    block = blockService.GetBlockIncludeTransactions(BlockLocator.ByHeight(height), true, previousBlockId).Result;
                }
                catch (AggregateException ae)
                {
                    ae.Handle(e =>
                    {
                        if (e is NxtLib.NxtException && e.Message == "Incorrect \"height\"")
                        {
                            return true;
                        }
                        return false;
                    });
                    hasMoreBlocks = false;
                    break;
                }
                foreach (var nxtTransaction in block.Transactions.Where(t => addressesSet.Contains(t.RecipientRs) && !t.Phased))
                {
                    logger.Invoke($"New incoming transaction ({nxtTransaction.TransactionId}), {nxtTransaction.Amount.Nxt} NXT was sent to {nxtTransaction.RecipientRs}");
                    var transaction = new Transaction
                    {
                        Address = nxtTransaction.RecipientRs,
                        Amount = nxtTransaction.Amount.Nxt,
                        Confirmed = nxtTransaction.Confirmations.HasValue,
                        Confirmations = nxtTransaction.Confirmations.Value,
                        TxId = nxtTransaction.TransactionId.ToString()
                    };
                    transactions.Add(transaction);
                }
                previousBlockId = block.BlockId;
            }

            if (transactions.Any())
            {
                logger.Invoke("Found some new deposits, will transfer funds to main account");

                var transactionsByAddress = transactions.GroupBy(t => t.Address)
                    .ToDictionary(grouping => grouping.Key, grouping => grouping.Sum(t => t.Amount));

                foreach (var transaction in transactionsByAddress)
                {
                    var account = depositAddresses.Single(a => a.Address == transaction.Key);
                    logger.Invoke($"Will internally send {transaction.Value - Fee.Nxt} NXT from {account.Address} (deposit account) to {mainAccount.Address} (main account)");
                    var secretPhrase = walletDb.GetSecretPhrase(account.Id);
                    SendToInternal(mainAccount.Address, transaction.Value, secretPhrase);
                }
            }

            walletDb.UpdateLastBlockId(previousBlockId);
            lastBlockId = previousBlockId;

            logger.Invoke($"Done with list transactions at {DateTime.Now}");
            return transactions;
        }

        /// <summary>
        /// Sends a given amount to a destination address, and returns transaction ID (hash)
        /// </summary>
        /// <param name="address">Destination address</param>
        /// <param name="amount">Amount to send</param>
        /// <returns>Transaction ID</returns>
        public string SendTo(string address, decimal amount)
        {
            logger.Invoke($"Starting to send {amount} NXT to {address}");
            var accountService = serviceFactory.CreateAccountService();
            var balance = accountService.GetBalance(mainAccount.Address).Result.Balance.Nxt;

            if (amount + Fee.Nxt > balance)
            {
                logger.Invoke($"Main account do not have enough funds. Available: {balance} NXT, Requested: {amount} NXT, Fee: {Fee.Nxt} NXT");
                throw new ArgumentException("Main account do not have enough funds to cover the transaction", nameof(amount));
            }
            
            var parameters = new NxtLib.CreateTransactionBySecretPhrase(true, 1440, Fee, mainAccount.SecretPhrase);
            var transaction = accountService.SendMoney(parameters, address, NxtLib.Amount.CreateAmountFromNxt(amount)).Result;

            logger.Invoke($"Done with sending {amount} NXT to {address}, txId is: {transaction.TransactionId}");
            return transaction.TransactionId.ToString();
        }

        /// <summary>
        /// Locks the wallets
        /// </summary>
        public void Lock()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Unlocks the wallet to send
        /// </summary>
        /// <param name="key">Current key</param>
        public void Unlock(string key)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Verifies if the wallet is currently encrypted
        /// </summary>
        /// <returns>True if wallet is encrypted, False otherwise</returns>
        public bool IsEncrypted()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Changes the encryption key
        /// </summary>
        /// <param name="key">Current key, or null if wallet is not encrypted yet</param>
        /// <param name="newKey">New key</param>
        public void ChangeKey(string key, string newKey)
        {
            throw new NotImplementedException();
        }

        private void InitWalletFile()
        {
            if (!walletDb.FileExists())
            {
                var serverInfoService = serviceFactory.CreateServerInfoService();
                var blockchainStatus = serverInfoService.GetBlockchainStatus().Result;
                var mainAccount = GenerateNewAccount();
                mainAccount.IsMainAccount = true;
                walletDb.InitNewDb(mainAccount, blockchainStatus.LastBlockId);
            }
        }

        private NxtAccount GenerateNewAccount()
        {
            var localPasswordGenerator = new LocalPasswordGenerator();
            var localAccountService = new LocalAccountService();

            var secretPhrase = localPasswordGenerator.GeneratePassword();
            var accountWithPublicKey = localAccountService.GetAccount(AccountIdLocator.BySecretPhrase(secretPhrase));

            var account = new NxtAccount
            {
                IsMainAccount = false,
                Address = accountWithPublicKey.AccountRs,
                SecretPhrase = secretPhrase
            };

            return account;
        }

        private void SendToInternal(string address, decimal balance, string secretPhrase)
        {
            var accountService = serviceFactory.CreateAccountService();
            var parameters = new NxtLib.CreateTransactionBySecretPhrase(true, 1440, Fee, secretPhrase);
            var amount = NxtLib.Amount.CreateAmountFromNxt(balance - parameters.Fee.Nxt);
            var transaction = accountService.SendMoney(parameters, address, amount).Result;
        }
    }
}
