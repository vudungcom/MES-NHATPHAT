// =====================================================================
// [GN-NG-TRACE 2026-10-28] ĐƯỜNG ĐI HÀNG LỖI CỦA 1 PO — chỉ đọc, không ghi DB, không đổi sổ.
//
// Tra theo TỪNG LẦN (WTS NG, NG đếm lúc nhận, báo lỗi sau khi nhận, phiếu giao / lần nhận hàng lỗi,
// huỷ, trả khách) — không tra từng chiếc (phiếu chuyển hàng lỗi không ghi lấy từ lần lỗi nào).
//
// Không công thức số riêng:
//   - Mỗi việc đọc từ chứng từ của sổ (LedgerInput) theo đúng thứ tự dòng B (HandoverLedgerService.ComputeSteps).
//   - Ô bên giao lấy hàng = đúng ô Compute() đã dùng (LedgerRow.TxFromCell).
//   - Số "đang giữ" = ô Tồn hàng lỗi + Tồn hàng sửa của sổ sau từng bước.
// Tự kiểm ở MỖI bước (cân hàng lỗi):
//   Σ phát sinh − Σ sửa xong về sản xuất − Σ huỷ − Σ trả khách = Σ đang giữ (sổ) + Σ đang chuyển (phiếu hàng lỗi chưa nhận)
//   Lệch ở bước nào thì đánh dấu bước đó + ghi vào Problems (bộ kiểm tools/GiaoNhanFuzz canh điều này).
// =====================================================================
using System;
using System.Collections.Generic;
using System.Linq;

namespace MES.Web.Services
{
    /// <summary>[GN-NG-TRACE] Thông tin phụ không có trong sổ: lý do lỗi trên WTS, người lập phiếu, người nhận.</summary>
    public class NgTraceExtra
    {
        public Dictionary<long, string?> WtsReason { get; set; } = new();
        public Dictionary<long, string?> TxWho     { get; set; } = new();
        public Dictionary<long, string?> RecvWho   { get; set; } = new();
    }

    /// <summary>[GN-NG-TRACE] Một lần dính hàng lỗi (một dòng tra cứu).</summary>
    public class NgTraceEvent
    {
        public int       Step   { get; set; }          // số dòng B của sổ theo bước
        public DateTime? At     { get; set; }
        /// <summary>WTS_NG | NHAN_NG | BAO_LOI | TRA_TRUOC | NHAN_VAO_LOI | GIAO_LOI | NHAN_LOI | VE_SX | NHAN_TRA | DONG | HUY | TRA_KHACH</summary>
        public string    Type   { get; set; } = "";
        public string    Group  { get; set; } = "";    // nơi làm việc này
        public string?   Other  { get; set; }          // nơi kia (giao tới / nhận từ)
        public string?   Code   { get; set; }          // NC (Gia công) / mã CV
        public decimal   Qty    { get; set; }
        public string?   Reason { get; set; }
        public string?   Who    { get; set; }
        public string?   DetectedAt { get; set; }      // nơi phát hiện lỗi (chỉ việc phát sinh)
        public string?   CausedBy   { get; set; }      // nơi gây lỗi (chỉ việc phát sinh)
        public string?   OriginText { get; set; }      // đầu vào / đầu ra (báo lỗi sau khi nhận)
        public string    Doc    { get; set; } = "";    // chứng từ: "Phiếu #12", "WTS #5", "Báo lỗi #3"
        public long      RefId  { get; set; }
        public string    Text   { get; set; } = "";
        public decimal   PhatSinh { get; set; }        // phần làm tăng tổng hàng lỗi
        public decimal   VeSx     { get; set; }        // phần sửa xong quay về hàng tốt
        public decimal   Ra       { get; set; }        // phần ra khỏi PO (huỷ / trả khách)
        // Sau bước này
        public decimal   DangGiu    { get; set; }
        public decimal   DangChuyen { get; set; }
        public List<(string Group, decimal Loi, decimal Sua)> GiuSau { get; set; } = new();
        public bool      Lech   { get; set; }

        public string Category => PhatSinh > 0 ? "PS" : PhatSinh < 0 ? "PS_BOT" : VeSx > 0 ? "XL" : Ra > 0 ? "RA" : "CHUYEN";
    }

    /// <summary>[GN-NG-TRACE] Tóm tắt 1 nơi.</summary>
    public class NgTraceGroup
    {
        public string  Code  { get; set; } = "";
        public string  Label { get; set; } = "";
        public decimal PhatHien { get; set; }   // lỗi phát hiện tại nơi này
        public decimal GayRa    { get; set; }   // lỗi do nơi này gây ra (theo chứng từ)
        public decimal LoiGiu   { get; set; }   // ô Tồn hàng lỗi của sổ
        public decimal SuaGiu   { get; set; }   // ô Tồn hàng sửa của sổ
        public decimal DaHuy    { get; set; }
        public decimal TraKhach { get; set; }
        public Dictionary<string, decimal> PhatHienTheoLoai { get; set; } = new();
        public LedgerExplain? LoiExplain { get; set; }
        public LedgerExplain? SuaExplain { get; set; }
    }

    public class NgTrace
    {
        public int     KhPlanDetailId { get; set; }
        public List<NgTraceEvent> Events { get; set; } = new();
        public List<NgTraceGroup> Groups { get; set; } = new();
        public decimal PhatSinh   { get; set; }
        public decimal VeSx       { get; set; }
        public decimal DaHuy      { get; set; }
        public decimal TraKhach   { get; set; }
        public decimal DangGiu    { get; set; }
        public decimal DangChuyen { get; set; }
        public bool    Balanced => PhatSinh - VeSx - DaHuy - TraKhach == DangGiu + DangChuyen && Problems.Count == 0;
        public bool    HasAny => Events.Count > 0;
        /// <summary>Chỗ khung lỗi không khớp sổ (không được xảy ra — báo lại để kiểm).</summary>
        public List<string> Problems { get; set; } = new();
        /// <summary>[VULOI 2026-10-28] Bảng theo từng vụ lỗi (mỗi dòng 1 vụ, cột = chặng). Đọc từ LedgerRow.Cases của sổ.</summary>
        public List<NgCaseTrace> Cases { get; set; } = new();
    }

    /// <summary>
    /// [VULOI 2026-10-28] Một dòng bảng "Theo từng vụ lỗi": Phát hiện → Nhận lỗi (+ tự xử lý) → Sửa → Hàng đã xử lý / Huỷ / Trả khách.
    /// Vụ nhiều chiếc tách nhánh vẫn 1 dòng, mỗi ô ghi số theo nhánh. Cân: Sinh ra = đã xử lý + huỷ + trả khách + đang giữ + đang chuyển.
    /// </summary>
    public class NgCaseTrace
    {
        public long     Id        { get; set; }
        public string   No        { get; set; } = "";
        public string   Loai      { get; set; } = "";
        public string   Nguon     { get; set; } = "";   // NG (WTS) / NG lúc nhận / Báo lỗi sau khi nhận / Hàng lỗi công đoạn trước / Nhận vào ô Hàng lỗi
        public DateTime? At       { get; set; }
        public string?  Who       { get; set; }
        public decimal  SinhRa    { get; set; }
        public string   PhatHien  { get; set; } = "";   // "Gia công NC1"
        public string   GayLoi    { get; set; } = "";   // nơi gây lỗi
        public string   NhanLoi   { get; set; } = "";   // "Kho 2 (tự xử lý)"
        public string   Sua       { get; set; } = "";   // "HTSP 3"
        public string   DaXuLy    { get; set; } = "";   // "Gia công 3"
        public decimal  DaXuLyQty { get; set; }
        public decimal  Huy       { get; set; }
        public string   HuyText   { get; set; } = "";
        public decimal  TraKhach  { get; set; }
        public string   KhachText { get; set; } = "";
        public string   DangGiu   { get; set; } = "";   // "Gia công 1" / "HTSP 2 (hàng sửa)"
        public decimal  DangGiuQty { get; set; }
        public decimal  DangChuyen { get; set; }
        public bool     Balanced  { get; set; }
        public bool     Done      => DangGiuQty == 0 && DangChuyen == 0;
        /// <summary>Các chặng theo thời gian (dòng chi tiết khi mở vụ).</summary>
        public List<LedgerCaseHop> Hops { get; set; } = new();
        /// <summary>Mọi nơi vụ đi qua (để lọc theo nơi).</summary>
        public HashSet<string> Places { get; set; } = new();
    }

    public static class HandoverNgTrace
    {
        static string GL(string? g) => HandoverLedgerService.GroupLabel(g ?? "");

        public static string TypeLabel(string t) => t switch
        {
            "WTS_NG"       => "NG (WTS)",
            "NHAN_NG"      => "NG lúc nhận",
            "BAO_LOI"      => "Báo lỗi sau khi nhận",
            "TRA_TRUOC"    => "Hàng lỗi công đoạn trước",
            "NHAN_VAO_LOI" => "Nhận vào ô Hàng lỗi / Hàng sửa",
            "GIAO_LOI"     => "Giao hàng lỗi",
            "NHAN_LOI"     => "Nhận hàng lỗi",
            "VE_SX"        => "Hàng lỗi đã xử lý",
            "NHAN_TRA"     => "Nhận lại hàng lỗi bị trả",
            "DONG"         => "Huỷ phần chưa nhận",
            "HUY"          => "Huỷ hàng lỗi",
            "TRA_KHACH"    => "Trả khách hàng lỗi",
            _ => t
        };

        /// <summary>[GN-NG-TRACE] PO từng có hàng lỗi (kể cả đã xử lý xong) — đọc thẳng các ô của sổ, dùng cho lọc trang Kế hoạch.</summary>
        public static bool EverHadNg(LedgerRow r)
        {
            if (r.DaHuy > 0 || r.TraKhach > 0) return true;
            // [EVER-NG-TP 2026-10-29] Kho đếm NG lúc nhận THÀNH PHẨM (Đóng gói → Kho) mà phiếu trả tự lập chưa được Đóng gói bấm Nhận:
            //   Kho không giữ ô thành phẩm, bên giao mới chỉ có "Chờ nhận" → không ô NG nào > 0 → lọc "có phát sinh lỗi" ở trang Kế hoạch
            //   bỏ sót PO trong khi khung hàng lỗi (PoLoi) đã hiện vụ. Bộ kiểm nặng bắt được (seed 84 LOI=1 PHOI=8, có từ trước bản này).
            //   Sổ vụ đã ghi số sinh ra → dùng làm dấu hiệu chung.
            if (r.Cases.Values.Any(c => c.SinhRa > 0)) return true;
            foreach (var g in r.Groups.Values)
            {
                if (g.NgCuoi > 0 || g.NgDau > 0 || g.NhanLoi > 0 || g.NhanSua > 0 || g.GiaoLoi > 0 || g.GiaoSua > 0 || g.LoiGiu > 0 || g.SuaGiu > 0) return true;
                if (g.Explains.TryGetValue("LoiGiu", out var e) && e.Terms.Any(t => t.Sign > 0 && t.Value > 0)) return true;
            }
            return false;
        }

        static decimal Held(LedgerRow r) => r.Groups.Values.Sum(g => g.LoiGiu + g.SuaGiu);

        /// <summary>[VULOI] Tên chặng của 1 vụ (Kind của LedgerCaseHop).</summary>
        public static string HopLabel(LedgerCaseHop h) => h.Kind switch
        {
            "PS"    => "Phát sinh",
            "GIAO"  => "Giao",
            "NHAN"  => "Nhận",
            "TRA"   => "NG lúc nhận, trả bên giao",
            "HUY"   => "Huỷ",
            "KHACH" => "Trả khách",
            _ => h.Kind
        };

        /// <summary>
        /// [VULOI 2026-10-28] Bảng theo từng vụ lỗi — chỉ đọc LedgerRow.Cases (sổ đã tính), không công thức riêng.
        /// Nơi trong mỗi chặng xếp theo lần đầu vụ tới nơi đó.
        /// </summary>
        public static List<NgCaseTrace> BuildCases(LedgerRow row)
        {
            var list = new List<NgCaseTrace>();
            foreach (var cs in row.Cases.Values.OrderBy(x => x.Id == 0 ? 1 : 0).ThenBy(x => x.At ?? DateTime.MinValue).ThenBy(x => x.Id))
            {
                // thứ tự nơi theo chặng đầu tiên vụ tới nơi đó
                var order = new List<string>();
                foreach (var h in cs.Hops) { if (!order.Contains(h.Group)) order.Add(h.Group); if (h.Other != null && !order.Contains(h.Other)) order.Add(h.Other); }
                string Join(Dictionary<string, decimal> d, Func<string, string>? suffix = null)
                    => string.Join(" → ", d.Where(x => x.Value != 0).OrderBy(x => order.IndexOf(x.Key) < 0 ? 99 : order.IndexOf(x.Key))
                                           .Select(x => $"{GL(x.Key)} {x.Value:0.##}" + (suffix?.Invoke(x.Key) ?? "")));
                var held = cs.Held.Where(x => x.Value != 0).OrderBy(x => order.IndexOf(x.Key.Split('|')[0]))
                    .Select(x => { var p = x.Key.Split('|'); return $"{GL(p[0])} {x.Value:0.##}" + (p[1] == HandoverCell.Sua ? " (hàng sửa)" : ""); });
                var t = new NgCaseTrace
                {
                    Id = cs.Id, No = cs.Id == 0 ? "—" : cs.No, Loai = cs.CatLabel,
                    Nguon = cs.Id == 0 ? "Dữ liệu trước khi có mã vụ" : NgOrigin.Label(cs.Origin),
                    At = cs.At, Who = cs.Who, SinhRa = cs.SinhRa,
                    PhatHien = cs.DetectGroup == null ? "—" : GL(cs.DetectGroup) + (string.IsNullOrEmpty(cs.DetectNc) ? "" : " " + (cs.DetectGroup == "GC" ? HandoverLedgerService.NcName(cs.DetectNc) : cs.DetectNc)),
                    GayLoi = cs.Cause == null ? "" : HandoverNgSource.CauseLabel(cs.Cause),
                    NhanLoi = Join(cs.NhanLoi, g => cs.TuXuLy.Contains(g) ? " (tự xử lý)" : ""),
                    Sua = Join(cs.NhanSua),
                    DaXuLy = Join(cs.DaXuLy), DaXuLyQty = cs.DaXuLyTotal,
                    Huy = cs.Huy, HuyText = Join(cs.HuyBy),
                    TraKhach = cs.TraKhach, KhachText = Join(cs.KhachBy),
                    DangGiu = string.Join(" · ", held), DangGiuQty = cs.HeldTotal, DangChuyen = cs.Transit,
                    Balanced = cs.Lech == 0,
                    Hops = cs.Hops.ToList(),
                };
                foreach (var g in order) t.Places.Add(g);
                list.Add(t);
            }
            return list;
        }

        /// <summary>
        /// Dựng đường đi hàng lỗi. steps: dòng B đã tính sẵn (truyền vào để khỏi tính lại); null thì tự tính.
        /// </summary>
        public static NgTrace Build(LedgerInput inp, NgTraceExtra? extra = null, List<LedgerStepRow>? steps = null)
        {
            extra ??= new NgTraceExtra();
            steps ??= HandoverLedgerService.ComputeSteps(inp);
            var final = HandoverLedgerService.Compute(inp);
            var tr = new NgTrace { KhPlanDetailId = inp.KhPlanDetailId };
            var gcLastNc = inp.GcSteps.Where(s => !s.IsBackup && !s.IsExcluded).OrderBy(s => s.StepOrder).LastOrDefault()?.NC;

            var txById = new Dictionary<long, InTx>();
            foreach (var t in inp.Txs) txById[t.Id] = t;
            var recvById = new Dictionary<long, (InTx t, InRecv r)>();
            foreach (var t in inp.Txs) foreach (var r in t.Receives) recvById[r.Id] = (t, r);
            var wtsById = new Dictionary<long, InWts>();
            foreach (var w in inp.Wts) wtsById[w.Id] = w;
            var repById = new Dictionary<long, InNgRep>();
            foreach (var b in inp.NgReps) repById[b.Id] = b;
            // Phiếu trả hàng lỗi phần mềm tự lập khi bên nhận đếm NG (giống Compute)
            var autoBySrc = new Dictionary<long, InTx>();
            foreach (var a in inp.Txs.Where(x => x.SourceReceiveId != null && !HandoverPurpose.IsOut(x.Purpose)))
                autoBySrc.TryAdd(a.SourceReceiveId!.Value, a);
            var recvIds = recvById.Keys.ToHashSet();

            string? FromCell(InTx t) => final.TxFromCell.TryGetValue(t.Id, out var c) ? c : null;
            static bool IsLoiCell(string? c) => c is HandoverCell.Loi or HandoverCell.Sua;

            decimal transit = 0, pool = 0;
            foreach (var st in steps)
            {
                var evs = new List<NgTraceEvent>();
                NgTraceEvent New(string type, string group, string? other, decimal qty, string doc, long refId, string text)
                {
                    var e = new NgTraceEvent { Step = st.Index, At = st.At, Type = type, Group = group, Other = other, Qty = qty, Doc = doc, RefId = refId, Text = text };
                    evs.Add(e);
                    return e;
                }
                // NG đếm lúc nhận (không vào ô Hàng lỗi): phần mềm trả bên giao — có phiếu trả tự lập thì hàng lỗi "đang chuyển"
                void NgBackTransit(InRecv r)
                {
                    if (autoBySrc.ContainsKey(r.Id)) transit += r.Ng;
                }
                string AutoNote(InRecv r) => autoBySrc.TryGetValue(r.Id, out var b) ? $" → phiếu trả #{b.Id}" : " → trả ngay";

                switch (st.Kind)
                {
                    case "WTS":
                    {
                        if (!wtsById.TryGetValue(st.RefId, out var w)) { tr.Problems.Add($"B{st.Index}: không thấy WTS #{st.RefId}"); break; }
                        if (w.Ng == 0) break;
                        var g = st.Group;
                        var code = w.WtsCode ?? w.NC;
                        var e = New("WTS_NG", g, null, w.Ng, $"WTS #{w.Id}", w.Id,
                            $"{GL(g)} làm hỏng {w.Ng:0.##}" + (string.IsNullOrEmpty(code) ? "" : (g == "GC" ? $" ở NC {code}" : $" ở mã {code}")) + $" (OK {w.Ok:0.##})");
                        e.Code = code; e.Who = w.Worker; e.PhatSinh = w.Ng; e.DetectedAt = g; e.CausedBy = g;
                        e.Reason = extra.WtsReason.TryGetValue(w.Id, out var rs) ? rs : null;
                        break;
                    }
                    case "BAOLOI":
                    {
                        if (!repById.TryGetValue(st.RefId, out var b)) { tr.Problems.Add($"B{st.Index}: không thấy báo lỗi #{st.RefId}"); break; }
                        var dauVao = b.Origin == HandoverNgSource.DauVao;
                        var e = New("BAO_LOI", b.Group, null, b.Qty, $"Báo lỗi #{b.Id}", b.Id,
                            $"{GL(b.Group)} phát hiện {b.Qty:0.##} lỗi sau khi nhận · lấy từ {HandoverNgSource.SourceLabel(b.Source, b.Group)}"
                            + (string.IsNullOrEmpty(b.NC) ? "" : $" NC {b.NC}"));
                        e.Code = b.NC; e.Who = b.Who; e.Reason = b.Reason; e.PhatSinh = b.Qty; e.DetectedAt = b.Group;
                        e.CausedBy = dauVao ? (b.Cause ?? "?") : b.Group;
                        e.OriginText = HandoverNgSource.OriginLabel(b.Origin, b.Cause);
                        break;
                    }
                    case "GIAO":
                    {
                        if (!txById.TryGetValue(st.RefId, out var t)) { tr.Problems.Add($"B{st.Index}: không thấy phiếu #{st.RefId}"); break; }
                        var purpose = HandoverLedgerService.Classify(t, gcLastNc);
                        var who = extra.TxWho.TryGetValue(t.Id, out var tw) ? tw : null;
                        if (HandoverPurpose.IsOut(t.Purpose))
                        {
                            var isScrap = t.Purpose == HandoverPurpose.Scrap;
                            var e = New(isScrap ? "HUY" : "TRA_KHACH", t.From, t.To, t.Issued, $"Phiếu #{t.Id}", t.Id,
                                $"{GL(t.From)} {(isScrap ? "huỷ" : "trả khách")} {t.Issued:0.##} hàng lỗi");
                            e.Ra = t.Issued; e.Reason = t.NgReason; e.Who = who;
                            break;
                        }
                        var fc = FromCell(t);
                        var q0 = t.Issued + t.Closed;   // số lúc lập phiếu (dòng B tính như vậy)
                        if (purpose == HandoverPurpose.NgReturn && fc == HandoverCell.Loi)
                        {
                            var e = New("TRA_TRUOC", t.From, t.To, q0, $"Phiếu #{t.Id}", t.Id,
                                $"{GL(t.From)} phát hiện {q0:0.##} lỗi công đoạn trước" + (string.IsNullOrEmpty(t.FromNC) ? "" : $" ở NC {t.FromNC}")
                                + $" → giao trả {GL(t.To)} ô {HandoverCell.Label(HandoverLedgerService.DesigCellOf(t, purpose))}");
                            e.Code = t.FromNC; e.Reason = t.NgReason; e.Who = who; e.PhatSinh = q0; e.DetectedAt = t.From; e.CausedBy = t.To;
                            transit += q0;
                        }
                        else if (IsLoiCell(fc))
                        {
                            var desig = HandoverLedgerService.DesigCellOf(t, purpose);
                            var viec = fc == HandoverCell.Sua ? "sửa hộ xong, trả"
                                     : desig == HandoverCell.Sua ? "gửi nhờ sửa"
                                     : desig == HandoverCell.Loi ? "chuyển hàng lỗi"
                                     : "tự sửa xong, trả";
                            var e = New("GIAO_LOI", t.From, t.To, q0, $"Phiếu #{t.Id}", t.Id,
                                $"{GL(t.From)} giao {q0:0.##} {HandoverCell.Label(fc)} ({viec}) → {GL(t.To)} ô {HandoverCell.Label(desig)}");
                            e.Reason = t.NgReason; e.Who = who;
                            transit += q0;
                        }
                        break;
                    }
                    case "DONG":
                    {
                        if (!txById.TryGetValue(st.RefId, out var t)) { tr.Problems.Add($"B{st.Index}: không thấy phiếu #{st.RefId}"); break; }
                        var fc = FromCell(t);
                        if (!IsLoiCell(fc) || HandoverPurpose.IsOut(t.Purpose)) break;
                        var purpose = HandoverLedgerService.Classify(t, gcLastNc);
                        transit -= t.Closed;
                        var prev = purpose == HandoverPurpose.NgReturn;
                        var e = New("DONG", t.From, t.To, t.Closed, $"Phiếu #{t.Id}", t.Id,
                            $"{GL(t.From)} huỷ phần chưa nhận {t.Closed:0.##} của phiếu hàng lỗi #{t.Id} → "
                            + (prev ? "hàng về lại chưa làm (không còn tính là lỗi)" : $"hàng lỗi về lại {GL(t.From)}"));
                        if (prev) { e.PhatSinh = -t.Closed; e.DetectedAt = t.From; e.CausedBy = t.To; }
                        break;
                    }
                    case "NHAN":
                    {
                        if (!recvById.TryGetValue(st.RefId, out var tr0)) { tr.Problems.Add($"B{st.Index}: không thấy lần nhận #{st.RefId}"); break; }
                        var (t, r) = tr0;
                        var who = extra.RecvWho.TryGetValue(r.Id, out var rw) ? rw : null;
                        var purpose = HandoverLedgerService.Classify(t, gcLastNc);
                        bool isAuto = t.SourceReceiveId != null && recvIds.Contains(t.SourceReceiveId.Value);
                        bool isHelp = purpose is HandoverPurpose.HelpSend or HandoverPurpose.HelpReturn;
                        var doc = $"Phiếu #{t.Id}";
                        if (isAuto)
                        {
                            var q = r.Ok + r.Ng;
                            transit -= q;
                            var e = New("NHAN_TRA", t.To, t.From, q, doc, r.Id,
                                $"{GL(t.To)} nhận lại {q:0.##} hàng lỗi {GL(t.From)} trả về (NG lúc nhận phiếu"
                                + (recvById.TryGetValue(t.SourceReceiveId!.Value, out var src) ? $" #{src.t.Id})" : ")"));
                            e.Reason = t.NgReason; e.Who = who;
                            break;
                        }
                        if (isHelp)
                        {
                            if (r.Ng > 0)
                            {
                                var e = New("NHAN_NG", t.To, t.From, r.Ng, doc, r.Id,
                                    $"{GL(t.To)} nhận hàng giao theo NC từ {GL(t.From)}, đếm thấy {r.Ng:0.##} lỗi{AutoNote(r)}");
                                e.Reason = r.NgReason; e.Who = who; e.PhatSinh = r.Ng; e.DetectedAt = t.To; e.CausedBy = t.From;
                                e.Code = t.FromNC ?? t.ToNC;
                                NgBackTransit(r);
                            }
                            break;
                        }
                        var fc = FromCell(t);
                        var desig = HandoverLedgerService.DesigCellOf(t, purpose);
                        var cell = HandoverCell.IsReceiveCell(r.Cell) ? r.Cell! : (desig ?? HandoverCell.Moi);   // giống Compute
                        var all = r.Ok + r.Ng;
                        if (IsLoiCell(fc))
                        {
                            transit -= all;
                            if (IsLoiCell(cell))
                            {
                                var e = New("NHAN_LOI", t.To, t.From, all, doc, r.Id,
                                    $"{GL(t.To)} nhận {all:0.##} vào ô {HandoverCell.Label(cell)} · từ {GL(t.From)}"
                                    + (r.Ng > 0 && cell != HandoverCell.Loi ? $" · {r.Ng:0.##} không nhận{AutoNote(r)}" : ""));
                                e.Who = who; e.Reason = r.Ng > 0 ? r.NgReason : null;
                            }
                            else
                            {
                                var e = New("VE_SX", t.To, t.From, r.Ok, doc, r.Id,
                                    $"{GL(t.To)} nhận {r.Ok:0.##} vào ô {HandoverCell.Label(cell)} (đã OK, làm tiếp) · từ {GL(t.From)}"
                                    + (r.Ng > 0 ? $" · {r.Ng:0.##} vẫn lỗi{AutoNote(r)}" : ""));
                                e.Who = who; e.VeSx = r.Ok; e.Reason = r.Ng > 0 ? r.NgReason : null;
                            }
                            if (r.Ng > 0 && cell != HandoverCell.Loi) NgBackTransit(r);
                        }
                        else if (IsLoiCell(cell))
                        {
                            // Hàng giao từ ô Hàng mới nhưng bên nhận ghi vào ô Hàng lỗi / Hàng sửa: từ đây là hàng lỗi
                            var e = New("NHAN_VAO_LOI", t.To, t.From, all, doc, r.Id,
                                $"{GL(t.To)} nhận {all:0.##} hàng {GL(t.From)} giao (ô Hàng mới) vào ô {HandoverCell.Label(cell)}");
                            e.Who = who; e.PhatSinh = all; e.DetectedAt = t.To; e.CausedBy = t.From; e.Reason = r.NgReason;
                            if (r.Ng > 0 && cell != HandoverCell.Loi) NgBackTransit(r);
                        }
                        else if (r.Ng > 0)
                        {
                            var e = New("NHAN_NG", t.To, t.From, r.Ng, doc, r.Id,
                                $"{GL(t.To)} nhận phiếu #{t.Id} từ {GL(t.From)}, đếm thấy {r.Ng:0.##} lỗi (nhận đạt {r.Ok:0.##}){AutoNote(r)}");
                            e.Who = who; e.PhatSinh = r.Ng; e.DetectedAt = t.To; e.CausedBy = t.From; e.Reason = r.NgReason; e.Code = t.ToNC;
                            NgBackTransit(r);
                        }
                        break;
                    }
                }
                pool += evs.Sum(e => e.PhatSinh - e.VeSx - e.Ra);
                var held = Held(st.Row);
                if (evs.Count == 0)
                {
                    // Bước không dính hàng lỗi thì tổng hàng lỗi của sổ cũng không được đổi
                    if (pool != held + transit)
                        tr.Problems.Add($"B{st.Index} ({st.Kind} #{st.RefId}): sổ đổi tồn hàng lỗi ({held:0.##} + đang chuyển {transit:0.##} ≠ {pool:0.##}) mà khung lỗi không nhận ra việc này");
                    continue;
                }
                var giu = HandoverLedgerService.GroupCodes
                    .Select(g => (g, st.Row.G(g).LoiGiu, st.Row.G(g).SuaGiu))
                    .Where(x => x.Item2 != 0 || x.Item3 != 0).ToList();
                var lech = pool != held + transit;
                if (lech) tr.Problems.Add($"B{st.Index}: tổng hàng lỗi theo chứng từ {pool:0.##} ≠ đang giữ {held:0.##} + đang chuyển {transit:0.##}");
                foreach (var e in evs)
                {
                    e.DangGiu = held; e.DangChuyen = transit; e.GiuSau = giu; e.Lech = lech;
                    tr.Events.Add(e);
                }
            }

            // ── Tổng + kiểm lại với sổ hiện tại ──
            tr.PhatSinh = tr.Events.Sum(e => e.PhatSinh);
            tr.VeSx     = tr.Events.Sum(e => e.VeSx);
            tr.DaHuy    = tr.Events.Where(e => e.Type == "HUY").Sum(e => e.Ra);
            tr.TraKhach = tr.Events.Where(e => e.Type == "TRA_KHACH").Sum(e => e.Ra);
            tr.DangGiu  = Held(final);
            tr.DangChuyen = transit;
            if (tr.DaHuy != final.DaHuy || tr.TraKhach != final.TraKhach)
                tr.Problems.Add($"Huỷ / trả khách theo khung lỗi {tr.DaHuy:0.##} / {tr.TraKhach:0.##} ≠ sổ {final.DaHuy:0.##} / {final.TraKhach:0.##}");
            // Đang chuyển tính độc lập từ phiếu hàng lỗi chưa nhận đủ
            decimal transitDoc = 0;
            foreach (var t in inp.Txs)
            {
                if (HandoverPurpose.IsOut(t.Purpose) || t.Status == "COMPLETED") continue;
                if (!IsLoiCell(FromCell(t))) continue;
                if (t.SourceReceiveId != null && !recvIds.Contains(t.SourceReceiveId.Value)) continue;
                transitDoc += Math.Max(0, t.Issued - t.Receives.Sum(x => x.Ok + x.Ng));
            }
            if (transitDoc != transit) tr.Problems.Add($"Đang chuyển theo từng bước {transit:0.##} ≠ theo phiếu chưa nhận {transitDoc:0.##}");
            if (tr.PhatSinh - tr.VeSx - tr.DaHuy - tr.TraKhach != tr.DangGiu + tr.DangChuyen)
                tr.Problems.Add($"Cân hàng lỗi: phát sinh {tr.PhatSinh:0.##} − xử lý xong {tr.VeSx:0.##} − huỷ {tr.DaHuy:0.##} − trả khách {tr.TraKhach:0.##} ≠ đang giữ {tr.DangGiu:0.##} + đang chuyển {tr.DangChuyen:0.##}");

            // ── Tóm tắt từng nơi ──
            foreach (var code in HandoverLedgerService.GroupCodes)
            {
                var G = final.G(code);
                var ng = new NgTraceGroup
                {
                    Code = code, Label = GL(code), LoiGiu = G.LoiGiu, SuaGiu = G.SuaGiu,
                    LoiExplain = G.Explains.TryGetValue("LoiGiu", out var le) ? le : null,
                    SuaExplain = G.Explains.TryGetValue("SuaGiu", out var se) ? se : null,
                    DaHuy    = tr.Events.Where(e => e.Type == "HUY" && e.Group == code).Sum(e => e.Ra),
                    TraKhach = tr.Events.Where(e => e.Type == "TRA_KHACH" && e.Group == code).Sum(e => e.Ra),
                };
                foreach (var e in tr.Events.Where(e => e.PhatSinh != 0))
                {
                    if (e.DetectedAt == code)
                    {
                        ng.PhatHien += e.PhatSinh;
                        var k = e.Type == "DONG" ? TypeLabel("TRA_TRUOC") : TypeLabel(e.Type);
                        ng.PhatHienTheoLoai[k] = (ng.PhatHienTheoLoai.TryGetValue(k, out var v) ? v : 0) + e.PhatSinh;
                    }
                    if (e.CausedBy == code) ng.GayRa += e.PhatSinh;
                }
                bool involved = tr.Events.Any(e => e.Group == code || e.Other == code);
                if (involved || ng.PhatHien != 0 || ng.GayRa != 0 || ng.LoiGiu != 0 || ng.SuaGiu != 0) tr.Groups.Add(ng);
            }
            // Phôi lỗi do khách (báo lỗi đầu vào ở Kho): nơi gây lỗi = khách
            var khach = tr.Events.Where(e => e.CausedBy == "KHACH").Sum(e => e.PhatSinh);
            if (khach != 0) tr.Groups.Add(new NgTraceGroup { Code = "KHACH", Label = "Phôi khách", GayRa = khach });
            tr.Cases = BuildCases(final);   // [VULOI]
            return tr;
        }
    }
}
