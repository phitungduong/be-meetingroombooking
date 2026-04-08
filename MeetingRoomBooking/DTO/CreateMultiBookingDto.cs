namespace MeetingRoomBooking.DTO
{
    public class CreateBookingDto
    {
        public int? MeetingRoomId { get; set; }     // dùng cho single
        public List<int>? MeetingRoomIds { get; set; } // dùng cho multi

        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
    }
}
