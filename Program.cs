using Market.CurrencyManager.RpcCoin.Connectors;
using NxtLib.Local;
using System;
using System.IO;

namespace Southxchange.Nxt
{
    class Program
    {
        private static string logfile = @"c:\temp\southxchange\log.txt";
        private static string walletfile = @"c:\temp\southxchange\nxtwallet.txt";
        private static IConnector connector;

        static void Main(string[] args)
        {
            Console.WriteLine("Welcome to the Southxchange Nxt Integration Program!");
            connector = new NxtConnector(walletfile, Constants.TestnetNxtUrl);
            connector.SetLogger(DoLog);

            WriteMenu();
        }

        private static void WriteMenu()
        {
            var done = false;
            while (!done)
            {
                Console.WriteLine();
                Console.WriteLine("1) Get Info");
                Console.WriteLine("2) Generate Address");
                Console.WriteLine("3) Is Address Valid");
                Console.WriteLine("4) List Transactions");
                Console.WriteLine("5) Send To");
                Console.WriteLine("6) Get Transaction Fees");
                Console.WriteLine("7) Ping");
                Console.WriteLine("8) Quit");
                Console.Write("> ");

                var value = int.Parse(Console.ReadLine());
                Console.WriteLine();

                switch (value)
                {
                    case 1:
                        WriteGetInfo();
                        break;
                    case 2:
                        WriteGenerateAddress();
                        break;
                    case 3:
                        WriteIsAddressValid();
                        break;
                    case 4:
                        WriteListTransactions();
                        break;
                    case 5:
                        WriteSendTo();
                        break;
                    case 6:
                        WriteGetTransactionFees();
                        break;
                    case 7:
                        WritePing();
                        break;
                    default:
                        done = true;
                        break;
                }
            }
        }

        private static void WriteGetInfo()
        {
            var info = connector.GetInfo();
            Console.WriteLine($"Connections: {info.Connections}");
            Console.WriteLine($"LastBlock: {info.LastBlock}");
            Console.WriteLine($"Reserves: {info.Reserves} NXT");
            Console.WriteLine($"Version: {info.Version}");
        }

        private static void WriteGenerateAddress()
        {
            Console.Write("How many addresses do you want to generate: ");
            var count = int.Parse(Console.ReadLine());
            for (int i = 0; i < count; i++)
            {
                var address = connector.GenerateAddress();
                Console.WriteLine($"Generated Address: {address}");
            }
        }

        private static void WriteIsAddressValid()
        {
            Console.Write("Enter address to test: ");
            var address = Console.ReadLine();
            var isValid = connector.IsAddressValid(address);
            Console.WriteLine($"Address validity: {isValid}");
        }

        private static void WriteListTransactions()
        {
            var transactions = connector.ListTransactions();
            Console.WriteLine($"Number of transactions: {transactions.Count}");
            foreach (var transaction in transactions)
            {
                Console.WriteLine($"Transaction Address: {transaction.Address}");
                Console.WriteLine($"Transaction Amount: {transaction.Amount} NXT");
                Console.WriteLine($"Transaction Confirmations: {transaction.Confirmations}");
                Console.WriteLine($"Transaction Confirmed: {transaction.Confirmed}");
                Console.WriteLine($"Transaction TxId: {transaction.TxId}");
                Console.WriteLine("---------------------------");
            }
        }

        private static void WriteSendTo()
        {
            Console.Write("Enter address to send to: ");
            var address = Console.ReadLine();
            var isValid = connector.IsAddressValid(address);
            if (!isValid)
            {
                Console.WriteLine("Invalid address!");
                return;
            }
            Console.Write("Enter amount (NXT) to send: ");
            var amount = decimal.Parse(Console.ReadLine());
            var txId = connector.SendTo(address, amount);
            Console.WriteLine($"Amount was sent, tx id: {txId}");
        }

        private static void WriteGetTransactionFees()
        {
            Console.Write("Enter TxId to check: ");
            var txId = Console.ReadLine();
            var fee = connector.GetTransactionFees(txId);
            Console.WriteLine($"Fee: {fee} NXT");
        }

        private static void WritePing()
        {
            Console.WriteLine("Starting to ping NxtConnector...");
            connector.Ping();
            Console.WriteLine("NxtConnector responded!");
        }

        public static void DoLog(string logString)
        {
            using (var streamwriter = new StreamWriter(logfile, true))
            {
                streamwriter.WriteLine(logString);
                streamwriter.Flush();
            }
        }
    }
}
