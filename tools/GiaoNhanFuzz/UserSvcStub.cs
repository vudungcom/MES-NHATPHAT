// [KH-GUARD] Stub thêm (chỉ để biên dịch) cho KhPlanService / KhoVatLieuService.
namespace MES.Web.Services
{
    public class UserService
    {
        public UserService(object? x) { }
        public Task<Dictionary<string, (bool CanView, bool CanEdit)>> GetAllUserPermissionsAsync(int userId)
            => Task.FromResult(new Dictionary<string, (bool CanView, bool CanEdit)>());
    }
}
