public class CreateBookingDto
{
    public int? MeetingRoomId { get; set; }   // giữ lại
    public List<int>? MeetingRoomIds { get; set; } // thêm dòng này

    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
}