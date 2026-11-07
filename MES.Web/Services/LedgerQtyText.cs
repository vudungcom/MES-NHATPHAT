using MES.Web.Data.Entities;

namespace MES.Web.Services
{
    // ════════════════════════════════════════════════════════════════════════════
    // [WTS-PANEL-QTY 2026-10-31] Chữ / màu / tooltip số lượng của 1 NC (GC) hoặc 1 mã CV (HTSP/KCS/PKG)
    //   — DÙNG CHUNG cho tab KH (View_Khplan) và bảng quy trình trang WTS (HieuSuat_Index), để 2 màn hình
    //   luôn ra cùng chữ, cùng số. Số lấy từ sổ chung HandoverLedgerService (LedgerStepLine), không tự cộng (L6).
    //   Trước: trang WTS tự cộng QtyDone (OK + NG gộp) rồi chia SL PO → "16/16" xanh khi thật là OK 15 + NG 1,
    //   mã sau ghi "/16" trong khi chỉ còn 15 cái vào làm. Tab KH đã đọc sổ từ [GN-WHERE][V5] 27/10.
    //   Số OK / NG của 1 dòng WTS: cùng công thức sổ dùng khi nạp WTS (HandoverLedgerService.LoadInputsAsync).
    // ════════════════════════════════════════════════════════════════════════════
    public static class LedgerQtyText
    {
        /// <summary>Dòng NC / mã CV trong sổ của 1 PO (null = mã không có dòng riêng: dự phòng gộp vào mã chính, hoặc chưa có sổ).</summary>
        public static LedgerStepLine? Step(LedgerRow? row, string pg, string? code)
        {
            if (row == null || string.IsNullOrEmpty(code)) return null;
            var g = pg switch { "GC" => "GC", "TARO" or "BAVIA" or "WASHING" => "HTSP", "KCS" => "KCS", "PKG" => "PKG", _ => "" };
            if (g == "") return null;
            return row.G(g).Steps.FirstOrDefault(x => x.Pg == pg && string.Equals(x.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));   // [WTS-MA-TRIM] trang gửi chữ snapshot thô, sổ giữ mã đã bỏ khoảng trắng
        }

        /// <summary>"OK 15 / vào 16 · NG 1" — mẫu số là số đã nhận vào NC / mã (theo sổ), không phải SL PO.</summary>
        public static string Text(LedgerStepLine? l, string? parentCode = null)
        {
            if (l == null) return string.IsNullOrEmpty(parentCode) ? "—" : $"gộp {parentCode}";
            if (l.IsPrep) return "chuẩn bị";
            return $"OK {l.Ok:0.##} / vào {(l.In ?? 0):0.##}" + (l.Ng > 0 ? $" · NG {l.Ng:0.##}" : "");
        }

        /// <summary>Màu badge Bootstrap: xanh = hết chờ, vàng = đang làm, đỏ = chưa làm / ô âm, xám = chuẩn bị / chưa có hàng vào.</summary>
        public static string Color(LedgerStepLine? l)
        {
            if (l == null || l.IsPrep || (l.In ?? 0) <= 0) return "secondary";
            if ((l.Waiting ?? 0) < 0) return "danger";
            if (l.Ok <= 0) return "danger";
            return (l.Waiting ?? 0) <= 0 ? "success" : "warning";
        }

        public static string Title(LedgerStepLine? l, string? parentCode = null)
        {
            if (l == null)
                return string.IsNullOrEmpty(parentCode)
                    ? "Chưa có số trong sổ Giao nhận"
                    : $"Dự phòng: số OK / NG đã cộng vào {parentCode} (theo sổ Giao nhận)";
            if (l.IsPrep) return "Mã chuẩn bị — chỉ tính giờ, không tính số lượng (N5)";
            return $"OK {l.Ok:0.##} · NG {l.Ng:0.##} · nhận vào {(l.In ?? 0):0.##} · còn chờ {(l.Waiting ?? 0):0.##} (theo sổ Giao nhận)";
        }

        /// <summary>OK của 1 dòng WTS — cùng công thức sổ (bản ghi cũ chưa có QtyOk: QtyDone − NG).</summary>
        public static decimal LogOk(WtsProductionLog w) => w.QtyOk ?? (w.QtyDone - (w.QtyNg ?? 0));

        /// <summary>NG của 1 dòng WTS — cùng công thức sổ.</summary>
        public static decimal LogNg(WtsProductionLog w) => w.QtyNg ?? 0;
    }
}
