namespace AmazonShipmentTool.Models;

public class AppointmentInfo
{
    public string AppointmentId { get; set; } = string.Empty;
    public string AppointmentReferenceCode { get; set; } = string.Empty;
    public string DestinationFC { get; set; } = string.Empty;
    public string CarrierSCAC { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string TrailerNumber { get; set; } = string.Empty;
    public string AppointmentType { get; set; } = string.Empty;
    public string FreightType { get; set; } = string.Empty;
    public string LoadType { get; set; } = string.Empty;
    public string ScheduledTime { get; set; } = string.Empty;
    public string CheckedInTime { get; set; } = string.Empty;
    public string CompletedTime { get; set; } = string.Empty;
    public string AppointmentCreationDate { get; set; } = string.Empty;
    public string EarliestArrivalTime { get; set; } = string.Empty;
    public string ArrivalTime { get; set; } = string.Empty;
    public string CarrierRequestedDeliveryDate { get; set; } = string.Empty;
    public string IsFreightClampable { get; set; } = string.Empty;
}
