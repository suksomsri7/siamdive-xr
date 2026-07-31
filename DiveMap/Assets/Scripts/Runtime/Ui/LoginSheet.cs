using System;
using UnityEngine;
using UnityEngine.UI;
using DiveMap.Core;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// Sign in / sign up — email → 6-digit code → username, with the admin address diverting to a
    /// passcode. Ported from the RN hub's login modal (siamdive-rn <c>src/app/map.tsx</c>):
    /// <code>
    ///   modalBg    rgba(0,0,0,.5) centred, padding 24
    ///   modalCard  maxWidth 360 · #0e2336 · r18 · padding 20
    ///   modalTitle #eaf4fb 16/600 · lgHint #9fb6c9 12 · lgErr #ff8a7a 13
    ///   modalInput #071a2b · r12 · 15 px · padding 13   (code step: centred, 22 px, spaced)
    ///   modalRow   two buttons, gap 10: cancel white 10% · ok #39b0e8 on #04121f
    /// </code>
    ///
    /// One thing this screen says that the web does not: verifying ADOPTS every map made on this
    /// device into the account and folds the device wallet in (email/verify/route.ts:50). That is
    /// not reversible by logging out again, so the player is told before the code is sent.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LoginSheet : MonoBehaviour
    {
        private enum Step { Email, Otp, Name, AdminPass }

        private static LoginSheet _open;

        private static readonly Color CardBg = new Color(0.055f, 0.137f, 0.212f, 1f);  // #0e2336
        private static readonly Color FieldBg = new Color(0.027f, 0.102f, 0.169f, 1f); // #071a2b
        private static readonly Color ErrFg = new Color(1f, 0.541f, 0.478f, 1f);       // #ff8a7a
        private static readonly Color CancelBg = new Color(1f, 1f, 1f, 0.10f);

        private RectTransform _card;
        private Text _title, _hint, _error;
        private InputField _field;
        private Text _okLabel;
        private Button _ok;

        private Step _step = Step.Email;
        private string _email = "";
        private bool _busy;

        /// <summary>Raised after a successful sign-in, so the hub can relabel itself.</summary>
        public static event Action SignedIn;

        // ── QC surface ───────────────────────────────────────────────────────────
        public static bool IsOpen => _open != null;
        public static LoginSheet Current => _open;
        public string StepName => _step.ToString();
        public string ErrorText => _error != null ? _error.text : null;

        public static void Open()
        {
            if (_open != null) return;
            RectTransform host = UiShell.Instance != null ? UiShell.Instance.OverlayRoot : null;
            if (host == null) return;

            var go = new GameObject("LoginSheet");
            go.transform.SetParent(host, false);
            var rt = go.AddComponent<RectTransform>();
            UiKit.Stretch(rt);
            var sheet = go.AddComponent<LoginSheet>();
            sheet.Build(rt);
            _open = sheet;
        }

        public static void Close()
        {
            if (_open == null) return;
            Destroy(_open.gameObject);
            _open = null;
        }

        private void OnDestroy()
        {
            if (_open == this) _open = null;
        }

        // ── build ────────────────────────────────────────────────────────────────

        private void Build(RectTransform root)
        {
            Button scrim = UiKit.MakeButton(root, "Scrim", null, 0, new Color(0f, 0f, 0f, 0.5f),
                                            Color.white, Close);
            UiKit.Stretch(scrim.GetComponent<RectTransform>());

            Image card = UiKit.MakeRounded(root, "Card", CardBg, 18f);
            _card = card.rectTransform;
            _card.anchorMin = new Vector2(0.5f, 0.5f);
            _card.anchorMax = new Vector2(0.5f, 0.5f);
            _card.pivot = new Vector2(0.5f, 0.5f);
            _card.sizeDelta = new Vector2(
                Mathf.Min(UiKit.Css(360f), Screen.width / UiKit.CanvasScale - UiKit.Css(48f)),
                UiKit.Css(300f));
            _card.anchoredPosition = Vector2.zero;

            float pad = UiKit.Css(20f);
            float y = pad;

            int tSize = UiKit.CssFont(16f);
            _title = UiKit.MakeLine(card.transform, "Title", "", tSize, TextAnchor.UpperLeft, UiKit.TextMain);
            _title.fontStyle = FontStyle.Bold;
            Row(_title.rectTransform, pad, y, UiKit.RowHeight(tSize));
            y += UiKit.LineHeight(tSize) + UiKit.Css(4f);

            int hSize = UiKit.CssFont(12f);
            _hint = UiKit.MakeText(card.transform, "Hint", "", hSize, TextAnchor.UpperLeft, UiKit.TextDim);
            Row(_hint.rectTransform, pad, y, UiKit.RowHeight(hSize, 3));
            y += UiKit.LineHeight(hSize) * 2f + UiKit.Css(10f);

            Image box = UiKit.MakeRounded(card.transform, "Field", FieldBg, 12f);
            Row(box.rectTransform, pad, y, UiKit.Css(48f));
            _field = UiKit.MakeInput(box.transform, "Input", "", UiKit.CssFont(15f));
            Image fbg = _field.GetComponent<Image>();
            if (fbg != null) fbg.color = new Color(0f, 0f, 0f, 0f);
            UiKit.Stretch(_field.GetComponent<RectTransform>());
            foreach (Graphic g in new Graphic[] { _field.textComponent, _field.placeholder })
            {
                if (g == null) continue;
                UiKit.Stretch(g.rectTransform);
                g.rectTransform.offsetMin = new Vector2(UiKit.Css(13f), 0f);
                g.rectTransform.offsetMax = new Vector2(-UiKit.Css(13f), 0f);
            }
            _field.onValueChanged.AddListener(_ => ClearError());
            y += UiKit.Css(48f) + UiKit.Css(6f);

            int eSize = UiKit.CssFont(13f);
            _error = UiKit.MakeText(card.transform, "Error", "", eSize, TextAnchor.UpperLeft, ErrFg);
            Row(_error.rectTransform, pad, y, UiKit.RowHeight(eSize, 2));
            y += UiKit.LineHeight(eSize) + UiKit.Css(10f);

            float btnH = UiKit.Css(46f);
            float half = (_card.sizeDelta.x - pad * 2f - UiKit.Css(10f)) * 0.5f;

            Button cancel = UiKit.MakeButton(card.transform, "Cancel", UiStrings.Tr("ปิด"),
                                             UiKit.CssFont(14f), CancelBg, UiKit.TextMain, Close);
            Round(cancel, 13f);
            RectTransform crt = cancel.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(0f, 1f);
            crt.pivot = new Vector2(0f, 1f);
            crt.sizeDelta = new Vector2(half, btnH);
            crt.anchoredPosition = new Vector2(pad, -y);

            _ok = UiKit.MakeButton(card.transform, "Ok", "", UiKit.CssFont(14f),
                                   UiKit.Accent, UiKit.OnAccent, Submit);
            Round(_ok, 13f);
            _okLabel = _ok.GetComponentInChildren<Text>();
            if (_okLabel != null) _okLabel.fontStyle = FontStyle.Bold;
            RectTransform ort = _ok.GetComponent<RectTransform>();
            ort.anchorMin = new Vector2(1f, 1f);
            ort.anchorMax = new Vector2(1f, 1f);
            ort.pivot = new Vector2(1f, 1f);
            ort.sizeDelta = new Vector2(half, btnH);
            ort.anchoredPosition = new Vector2(-pad, -y);
            y += btnH;

            _card.sizeDelta = new Vector2(_card.sizeDelta.x, y + pad);
            SetStep(Step.Email);
        }

        private static void Round(Button b, float radius)
        {
            Image img = b.GetComponent<Image>();
            if (img == null) return;
            img.sprite = UiKit.RoundedSprite(radius);
            img.type = Image.Type.Sliced;
        }

        private static void Row(RectTransform rt, float pad, float y, float h)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-pad * 2f, h);
            rt.anchoredPosition = new Vector2(0f, -y);
        }

        // ── steps ────────────────────────────────────────────────────────────────

        private void SetStep(Step step)
        {
            _step = step;
            ClearError();
            _field.text = "";
            _field.contentType = InputField.ContentType.Standard;
            _field.characterLimit = 0;
            _field.textComponent.alignment = TextAnchor.MiddleLeft;
            _field.textComponent.fontSize = UiKit.CssFont(15f);

            switch (step)
            {
                case Step.Email:
                    _title.text = UiStrings.Tr("เข้าสู่ระบบ / สมัครสมาชิก");
                    // The web does not warn about this; it should. Verifying moves this device's
                    // maps and coins into the account for good.
                    _hint.text = UiStrings.Tr("ใส่อีเมลเพื่อรับรหัส OTP") + "\n" +
                                 UiStrings.Tr("แมพและเหรียญในเครื่องนี้จะถูกผูกเข้าบัญชี");
                    Placeholder("you@email.com");
                    _field.contentType = InputField.ContentType.EmailAddress;
                    _okLabel.text = UiStrings.Tr("ส่งรหัส");
                    break;

                case Step.Otp:
                    _title.text = UiStrings.Tr("ใส่รหัส 6 หลัก");
                    _hint.text = UiStrings.Tr("ส่งไปที่") + " " + _email;
                    Placeholder("------");
                    _field.contentType = InputField.ContentType.IntegerNumber;
                    _field.characterLimit = Account.OtpDigits;
                    _field.textComponent.alignment = TextAnchor.MiddleCenter;
                    _field.textComponent.fontSize = UiKit.CssFont(22f);
                    _okLabel.text = UiStrings.Tr("ยืนยัน");
                    break;

                case Step.AdminPass:
                    _title.text = UiStrings.Tr("เข้าสู่ระบบแอดมิน");
                    _hint.text = UiStrings.Tr("ใส่ passcode แอดมิน");
                    Placeholder("passcode");
                    _field.contentType = InputField.ContentType.Password;
                    _field.textComponent.alignment = TextAnchor.MiddleCenter;
                    _okLabel.text = UiStrings.Tr("เข้าสู่ระบบ");
                    break;

                case Step.Name:
                    _title.text = UiStrings.Tr("ตั้งชื่อผู้ใช้");
                    _hint.text = UiStrings.Tr("3-20 ตัว · ไทย/อังกฤษ/เลข/เว้นวรรค/_");
                    Placeholder(UiStrings.Tr("ชื่อของคุณ"));
                    _field.characterLimit = Account.NameMax;
                    _okLabel.text = UiStrings.Tr("เสร็จ");
                    break;
            }
            Debug.Log("[Account] login step=" + step);
        }

        private void Placeholder(string text)
        {
            if (_field.placeholder is Text ph) ph.text = text;
        }

        private void ClearError() => SetError(null);

        private void SetError(string thai)
        {
            if (_error == null) return;
            _error.text = string.IsNullOrEmpty(thai) ? "" : UiStrings.Tr(thai);
        }

        private void Busy(bool on)
        {
            _busy = on;
            if (_ok != null) _ok.interactable = !on;
            if (_okLabel != null && on) _okLabel.text = "…";
        }

        // ── submit ───────────────────────────────────────────────────────────────

        private void Submit()
        {
            if (_busy) return;
            string value = _field != null ? _field.text : "";

            switch (_step)
            {
                case Step.Email: StartCoroutine(DoEmail(value)); break;
                case Step.Otp: StartCoroutine(DoVerify(value)); break;
                case Step.AdminPass: StartCoroutine(DoAdmin(value)); break;
                case Step.Name: StartCoroutine(DoName(value)); break;
            }
        }

        private System.Collections.IEnumerator DoEmail(string email)
        {
            string mail = (email ?? "").Trim();
            if (!Account.IsValidEmail(mail)) { SetError("อีเมลไม่ถูกต้อง"); yield break; }

            _email = mail;
            // The admin address never gets an OTP — it takes the passcode instead.
            if (Account.IsAdminEmail(mail)) { SetStep(Step.AdminPass); yield break; }

            Busy(true);
            string err = null;
            yield return AccountClient.RequestOtp(mail, e => err = e);
            Busy(false);

            if (err != null) { SetStep(Step.Email); _field.text = mail; SetError(ErrorFor(err)); yield break; }
            SetStep(Step.Otp);
        }

        private System.Collections.IEnumerator DoVerify(string code)
        {
            if (!Account.IsValidOtp(code)) { SetError("รหัสผิด"); yield break; }

            Busy(true);
            bool needName = false; string err = null;
            yield return AccountClient.Verify(_email, code, (n, e) => { needName = n; err = e; });
            Busy(false);

            if (err != null) { SetStep(Step.Otp); SetError(ErrorFor(err)); yield break; }
            if (needName) { SetStep(Step.Name); yield break; }
            Finish();
        }

        private System.Collections.IEnumerator DoAdmin(string passcode)
        {
            if (string.IsNullOrWhiteSpace(passcode)) yield break;

            Busy(true);
            string err = null;
            yield return AccountClient.AdminLogin(passcode, e => err = e);
            Busy(false);

            if (err != null) { SetStep(Step.AdminPass); SetError(ErrorFor(err)); yield break; }
            Finish();
        }

        private System.Collections.IEnumerator DoName(string name)
        {
            string problem = Account.NameError(name);
            if (problem != null) { SetError(problem); yield break; }

            Busy(true);
            string err = null;
            yield return AccountClient.SetUsername(_email, name, e => err = e);
            Busy(false);

            if (err != null) { SetStep(Step.Name); _field.text = name; SetError(ErrorFor(err)); yield break; }
            Finish();
        }

        private void Finish()
        {
            Debug.Log($"[Account] signed in as '{Account.Name}' ({Account.Email})");
            Toast.ShowTr("เข้าสู่ระบบแล้ว");
            Close();
            SignedIn?.Invoke();
        }

        /// <summary>The routes answer with machine keys; turn each into the sentence RN shows.</summary>
        private static string ErrorFor(string key)
        {
            switch (key)
            {
                case "too_soon":      return "เพิ่งส่งไป รอสักครู่";
                case "invalid_email": return "อีเมลไม่ถูกต้อง";
                case "wrong_code":
                case "otp_invalid":   return "รหัสผิด";
                case "expired":       return "รหัสหมดอายุ";
                case "name_taken":    return "ชื่อนี้มีคนใช้แล้ว ลองชื่ออื่น";
                case "name_reserved": return "ชื่อนี้สงวนไว้";
                case "invalid_name":  return "ชื่อสั้นไป (3-20 ตัว)";
                case "wrong_passcode": return "passcode ผิด";
                case "upstream_unreachable": return "เชื่อมต่อไม่ได้";
                default:              return "เชื่อมต่อไม่ได้";
            }
        }
    }
}
