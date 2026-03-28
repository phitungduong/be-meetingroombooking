namespace MeetingRoomBooking.DTO
{
    public class UpdateBookingDto
    {
        public int MeetingRoomId { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string? Status { get; set; }
    }
}
