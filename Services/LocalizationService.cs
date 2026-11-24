namespace RavUninstallerTool.Services;

/// <summary>Live EN/VI localization. Call <see cref="SetLanguage"/> and listen to <see cref="LanguageChanged"/>.</summary>
public static class LocalizationService
{
    private static string _lang = "vi";

    public static event Action? LanguageChanged;

    public static string Language => _lang;

    public static void SetLanguage(string lang)
    {
        lang = lang.Equals("en", StringComparison.OrdinalIgnoreCase) ? "en" : "vi";
        if (lang == _lang) return;
        _lang = lang;
        LanguageChanged?.Invoke();
    }

    public static void Toggle() => SetLanguage(_lang == "vi" ? "en" : "vi");

    public static string T(string key, params object[] args)
    {
        var dict = _lang == "vi" ? Vi : En;
        if (!dict.TryGetValue(key, out var s) && !En.TryGetValue(key, out s)) s = key;
        if (args.Length == 0) return s;
        try { return string.Format(s, args); } catch (FormatException) { return s; }
    }

    private static readonly Dictionary<string, string> En = new()
    {
        ["app_title"] = "RAV Uninstaller Support Tool",
        ["btn_theme_tip"] = "Toggle light/dark theme",
        ["btn_lang_tip"] = "Switch language (VI/EN)",
        ["btn_update"] = "Check Update",
        ["btn_report"] = "Report / Log",
        ["btn_about"] = "About",
        ["btn_close"] = "Close",
        ["btn_copy"] = "Copy",
        ["card_os"] = "System & Defender",
        ["card_rav"] = "RAV Detection",
        ["card_env"] = "Environment & Restore",
        ["btn_rescan"] = "Rescan",
        ["tab_folders"] = "Folders",
        ["tab_registry"] = "Registry",
        ["tab_services"] = "Services",
        ["tab_drivers"] = "Drivers",
        ["tab_tasks"] = "Tasks",
        ["tab_extras"] = "Shortcuts & Policy",
        ["col_name"] = "Name / Path",
        ["col_detail"] = "Detail",
        ["col_state"] = "State",
        ["group_manual"] = "Manual",
        ["group_auto"] = "Automatic (Safe Mode)",
        ["btn_manual"] = "1. Manual Uninstall (Uninstall.exe)",
        ["btn_quick"] = "2. Quick Clean Remnants (No Reboot)",
        ["btn_auto"] = "3. Auto Safe Mode + Clean All",
        ["ctx_explorer"] = "Open in Explorer",
        ["ctx_regedit"] = "Open in Regedit",
        ["ctx_copy"] = "Copy Path",
        ["status_ready"] = "Ready.",
        ["status_scanning"] = "Scanning system...",
        ["status_scan_done"] = "Scan finished: {0} item(s) found.",
        ["status_scan_failed"] = "Scan failed: {0}",
        ["rav_found"] = "RAV detected: {0} item(s)",
        ["rav_clean"] = "No RAV remnants found",
        ["rav_active"] = "Active services/drivers present",
        ["rav_inactive"] = "No active services/drivers",
        ["os_line"] = "{0} ({1})",
        ["defender_line"] = "Defender / AV: {0}",
        ["os_unsupported"] = "This tool requires Windows 10 or later.",
        ["not_installed"] = "RAV Endpoint Protection / ReasonLabs is not installed on this computer (0 items found).",
        ["env_mode"] = "Mode: {0}",
        ["mode_normal"] = "Normal",
        ["mode_safe"] = "Safe Mode",
        ["env_restore"] = "Restore point is created automatically before cleaning.",
        ["step_idle"] = "Idle",
        ["step_restore"] = "Creating restore point...",
        ["step_clean"] = "Running cleaner script...",
        ["step_prepare_safe"] = "Preparing Safe Mode...",
        ["step_safe_clean"] = "Cleaning in Safe Mode...",
        ["step_verify"] = "Verifying after cleanup...",
        ["step_done"] = "Completed",
        ["step_reboot"] = "Rebooting...",
        ["confirm_title"] = "Confirmation",
        ["confirm_auto"] = "The computer will reboot into Safe Mode, remove RAV / ReasonLabs automatically, then reboot back to normal Windows.\n\nSave your work first. Continue?",
        ["confirm_quick"] = "Remove all detected RAV remnants now without rebooting?",
        ["no_uninstaller"] = "Uninstall.exe of RAV was not found. Use Quick Clean or Auto Safe Mode instead.",
        ["uninstaller_started"] = "Started: {0}",
        ["error_title"] = "Error",
        ["info_title"] = "Information",
        ["clean_done"] = "Cleanup finished. Rescan to verify.",
        ["clean_failed"] = "Cleanup failed: {0}",
        ["safe_prepare_failed"] = "Could not prepare Safe Mode: {0}",
        ["safe_clean_title"] = "Safe Mode cleanup is running. Please do not turn off the computer.",
        ["post_verify_title"] = "Final verification",
        ["post_verify_ok"] = "RAV has been removed completely.",
        ["post_verify_left"] = "Some items remain ({0}). See the report.",
        ["update_checking"] = "Checking for updates...",
        ["update_latest"] = "You are running the latest version ({0}).",
        ["update_available"] = "New version available: {0}\n{1}",
        ["update_failed"] = "Could not check for updates (offline?).",
        ["update_confirm_install"] = "A new version {0} is available. Download and install it now?",
        ["update_downloading"] = "Downloading update...",
        ["update_verifying"] = "Verifying download (SHA-256)...",
        ["update_applying"] = "Applying update and restarting...",
        ["update_apply_failed"] = "Update failed: {0}",
        ["about_title"] = "About",
        ["about_text"] =
            "RAV Uninstaller Support Tool v{0}\r\n" +
            "==================================================\r\n" +
            "Purpose:\r\n" +
            "  Inspects and completely removes RAV Endpoint Protection / ReasonLabs\r\n" +
            "  (including kernel drivers, services, registry keys, tasks, shortcuts,\r\n" +
            "  and forced browser extension policies) on Windows 10 & Windows 11.\r\n\r\n" +
            "Key Features:\r\n" +
            "  • Automatic System Restore Point creation before removal\r\n" +
            "  • 6-category deep scan (64-bit & 32-bit registry views)\r\n" +
            "  • Manual Mode (official Uninstall.exe & quick remnant cleanup)\r\n" +
            "  • Automatic Safe Mode cleanup with boot-loop protection\r\n" +
            "  • Post-clean verification & Windows Defender status check\r\n" +
            "  • Automatic log export to Desktop (RAVCleaner.log) & temp cleanup\r\n\r\n" +
            "Technical Stack:\r\n" +
            "  • .NET 10 WinForms (Self-Contained Single-File win-x64) + Guna.UI2\r\n" +
            "  • Embedded PowerShell deep-clean engine (RAV.ps1)\r\n\r\n" +
            "CLI Flags (for technicians):\r\n" +
            "  --scan             Open and scan system\r\n" +
            "  --clean-remnants   Run quick remnant cleanup\r\n" +
            "  --auto-safemode    Prepare Safe Mode cleanup and reboot",
        ["report_title"] = "Report / Log",
        ["restore_ok"] = "Restore point created.",
        ["restore_warn"] = "Restore point not created (non-fatal): {0}",
        ["copied"] = "Copied to clipboard.",
        ["not_admin"] = "Administrator rights are required.",
        ["cli_unknown"] = "Unknown argument: {0}",
    };

    private static readonly Dictionary<string, string> Vi = new()
    {
        ["app_title"] = "Công cụ hỗ trợ gỡ RAV",
        ["btn_theme_tip"] = "Đổi giao diện sáng/tối",
        ["btn_lang_tip"] = "Đổi ngôn ngữ (VI/EN)",
        ["btn_update"] = "Kiểm tra cập nhật",
        ["btn_report"] = "Báo cáo / Log",
        ["btn_about"] = "Giới thiệu",
        ["btn_close"] = "Đóng",
        ["btn_copy"] = "Sao chép",
        ["card_os"] = "Hệ thống & Defender",
        ["card_rav"] = "Phát hiện RAV",
        ["card_env"] = "Môi trường & Khôi phục",
        ["btn_rescan"] = "Quét lại",
        ["tab_folders"] = "Thư mục",
        ["tab_registry"] = "Registry",
        ["tab_services"] = "Dịch vụ",
        ["tab_drivers"] = "Driver",
        ["tab_tasks"] = "Tác vụ",
        ["tab_extras"] = "Shortcut & Chính sách",
        ["col_name"] = "Tên / Đường dẫn",
        ["col_detail"] = "Chi tiết",
        ["col_state"] = "Trạng thái",
        ["group_manual"] = "Thủ công",
        ["group_auto"] = "Tự động (Safe Mode)",
        ["btn_manual"] = "1. Gỡ thủ công (Uninstall.exe)",
        ["btn_quick"] = "2. Dọn nhanh tàn dư (Không khởi động lại)",
        ["btn_auto"] = "3. Tự động Safe Mode + Dọn sạch",
        ["ctx_explorer"] = "Mở trong Explorer",
        ["ctx_regedit"] = "Mở trong Regedit",
        ["ctx_copy"] = "Sao chép đường dẫn",
        ["status_ready"] = "Sẵn sàng.",
        ["status_scanning"] = "Đang quét hệ thống...",
        ["status_scan_done"] = "Quét xong: tìm thấy {0} mục.",
        ["status_scan_failed"] = "Quét thất bại: {0}",
        ["rav_found"] = "Phát hiện RAV: {0} mục",
        ["rav_clean"] = "Không còn tàn dư RAV",
        ["rav_active"] = "Còn dịch vụ/driver đang hoạt động",
        ["rav_inactive"] = "Không có dịch vụ/driver hoạt động",
        ["os_line"] = "{0} ({1})",
        ["defender_line"] = "Defender / AV: {0}",
        ["os_unsupported"] = "Công cụ yêu cầu Windows 10 trở lên.",
        ["not_installed"] = "Không phát hiện RAV Endpoint Protection / ReasonLabs trên máy tính này (0 mục).",
        ["env_mode"] = "Chế độ: {0}",
        ["mode_normal"] = "Bình thường",
        ["mode_safe"] = "Safe Mode",
        ["env_restore"] = "Điểm khôi phục được tạo tự động trước khi dọn dẹp.",
        ["step_idle"] = "Chờ",
        ["step_restore"] = "Đang tạo điểm khôi phục...",
        ["step_clean"] = "Đang chạy script dọn dẹp...",
        ["step_prepare_safe"] = "Đang chuẩn bị Safe Mode...",
        ["step_safe_clean"] = "Đang dọn dẹp trong Safe Mode...",
        ["step_verify"] = "Đang kiểm tra sau dọn dẹp...",
        ["step_done"] = "Hoàn tất",
        ["step_reboot"] = "Đang khởi động lại...",
        ["confirm_title"] = "Xác nhận",
        ["confirm_auto"] = "Máy sẽ khởi động lại vào Safe Mode, tự động gỡ RAV / ReasonLabs, rồi khởi động lại về Windows bình thường.\n\nHãy lưu công việc trước. Tiếp tục?",
        ["confirm_quick"] = "Xóa ngay tất cả tàn dư RAV đã phát hiện mà không khởi động lại?",
        ["no_uninstaller"] = "Không tìm thấy Uninstall.exe của RAV. Hãy dùng Dọn nhanh hoặc Tự động Safe Mode.",
        ["uninstaller_started"] = "Đã chạy: {0}",
        ["error_title"] = "Lỗi",
        ["info_title"] = "Thông tin",
        ["clean_done"] = "Đã dọn xong. Hãy quét lại để kiểm tra.",
        ["clean_failed"] = "Dọn dẹp thất bại: {0}",
        ["safe_prepare_failed"] = "Không thể chuẩn bị Safe Mode: {0}",
        ["safe_clean_title"] = "Đang dọn dẹp trong Safe Mode. Vui lòng không tắt máy.",
        ["post_verify_title"] = "Kiểm tra cuối cùng",
        ["post_verify_ok"] = "RAV đã được gỡ hoàn toàn.",
        ["post_verify_left"] = "Vẫn còn một số mục ({0}). Xem báo cáo.",
        ["update_checking"] = "Đang kiểm tra cập nhật...",
        ["update_latest"] = "Bạn đang dùng phiên bản mới nhất ({0}).",
        ["update_available"] = "Có phiên bản mới: {0}\n{1}",
        ["update_failed"] = "Không thể kiểm tra cập nhật (có thể đang offline).",
        ["update_confirm_install"] = "Có phiên bản mới {0}. Bạn có muốn tải và cài đặt ngay bây giờ không?",
        ["update_downloading"] = "Đang tải bản cập nhật...",
        ["update_verifying"] = "Đang xác minh bản tải về (SHA-256)...",
        ["update_applying"] = "Đang áp dụng cập nhật và khởi động lại...",
        ["update_apply_failed"] = "Cập nhật thất bại: {0}",
        ["about_title"] = "Giới thiệu",
        ["about_text"] =
            "Công cụ hỗ trợ gỡ RAV (RAV Uninstaller Support Tool) v{0}\r\n" +
            "==================================================\r\n" +
            "Mục đích:\r\n" +
            "  Kiểm tra và gỡ bỏ triệt để RAV Endpoint Protection / ReasonLabs\r\n" +
            "  (bao gồm kernel driver, service, registry, scheduled task, shortcut\r\n" +
            "  và chính sách ép cài extension trình duyệt) trên Windows 10 & 11.\r\n\r\n" +
            "Tính năng chính:\r\n" +
            "  • Tự động tạo System Restore Point ngầm trước khi dọn dẹp\r\n" +
            "  • Quét sâu 6 nhóm thành phần (hỗ trợ cả Registry 64-bit & 32-bit)\r\n" +
            "  • Chế độ Thủ công: mở Uninstall.exe gốc hoặc dọn nhanh tàn dư\r\n" +
            "  • Chế độ Tự động: vào Safe Mode chạy ngầm & tự quay về Win thường\r\n" +
            "  • Hậu kiểm trạng thái Windows Defender sau khi gỡ sạch\r\n" +
            "  • Tự xuất RAVCleaner.log ra Desktop & dọn sạch file tạm của tool\r\n\r\n" +
            "Công nghệ:\r\n" +
            "  • .NET 10 WinForms (Single-File Self-Contained win-x64) + Guna.UI2\r\n" +
            "  • Nhúng sẵn engine PowerShell dọn sâu (RAV.ps1)\r\n\r\n" +
            "Tham số dòng lệnh (cho kỹ thuật viên):\r\n" +
            "  --scan             Mở tool và quét hệ thống\r\n" +
            "  --clean-remnants   Chạy dọn nhanh tàn dư không cần reboot\r\n" +
            "  --auto-safemode    Chuẩn bị Safe Mode và tự động gỡ sạch",
        ["report_title"] = "Báo cáo / Log",
        ["restore_ok"] = "Đã tạo điểm khôi phục.",
        ["restore_warn"] = "Không tạo được điểm khôi phục (không nghiêm trọng): {0}",
        ["copied"] = "Đã sao chép.",
        ["not_admin"] = "Cần quyền Administrator.",
        ["cli_unknown"] = "Tham số không hợp lệ: {0}",
    };
}
