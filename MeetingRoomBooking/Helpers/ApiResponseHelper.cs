namespace MeetingRoomBooking.Helpers
{
    public static class ApiResponseHelper
    {
        public static ApiResponse<T> Success<T>(T data, string message = "Success")
        {
            return new ApiResponse<T>(true, message, data);
        }

        public static ApiResponse<T> Fail<T>(string message)
        {
            return new ApiResponse<T>(false, message, default);
        }
    }
}
