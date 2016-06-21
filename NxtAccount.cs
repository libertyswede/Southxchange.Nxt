namespace Southxchange.Nxt
{
    class NxtAccount
    {
        public string SecretPhrase { get; set; }
        public string PublicKey { get; set; }
        public string Address { get; set; }
        public ulong LastKnownBlockId { get; set; }
    }
}
