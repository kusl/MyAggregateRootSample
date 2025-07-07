namespace MyClassLibrary;
public class CustomerBusinessRules
{
    public int MaxOutstandingOrders { get; set; } = 10;
    public int OutstandingOrderDays { get; set; } = 30;
}