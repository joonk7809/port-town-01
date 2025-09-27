namespace PortTown01.Core
{
    public enum ContractType { Employment, Purchase, Haul, GuardDuty }
    public enum ContractState { Draft, Active, Completed, Failed, Cancelled }

    public class Contract
    {
        public int Id;
        public ContractType Type;
        public ContractState State;

        // Employment terms
        public int EmployerId;        // agent paying wages
        public int EmployeeId;        // agent receiving wages
        public int WorksiteId;        // site where work is performed
        public float WagePerMin;      // coins per minute

        // simple bookkeeping
        public float AccruedSinceLastPay = 0f; // seconds worked since last payout
    }
}
