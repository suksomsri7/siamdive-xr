using System;
using UnityEngine;
using UnityEngine.UI;
using DiveMap.Core;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// The account sheet behind the hub's round avatar button — RN's <c>profileOpen</c> modal:
    /// a 64 px initial, the username in gold, the email, Close / Log out, and "Delete account"
    /// in red underneath.
    ///
    /// Deleting is confirmed in a second step rather than fired on the first tap: the RN app puts
    /// it behind <c>Alert.alert</c>, and this is the one control on the screen that cannot be
    /// undone.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProfileSheet : MonoBehaviour
    {
        private static ProfileSheet _open;

        private static readonly Color CardBg = new Color(0.055f, 0.137f, 0.212f, 1f);  // #0e2336
        private static readonly Color AvatarBg = new Color(0.110f, 0.455f, 0.690f, 1f); // #1c74b0
        private static readonly Color NameFg = new Color(1f, 0.835f, 0.290f, 1f);       // #ffd54a
        private static readonly Color DangerFg = new Color(0.898f, 0.282f, 0.302f, 1f); // #e5484d
        private static readonly Color CancelBg = new Color(1f, 1f, 1f, 0.10f);

        /// <summary>Raised after logout or deletion, so the hub can drop its "by You" labels.</summary>
        public static event Action Changed;

        private Text _delete;
        private bool _armed;

        public static bool IsOpen => _open != null;

        public static void Open()
        {
            if (_open != null) return;
            RectTransform host = UiShell.Instance != null ? UiShell.Instance.OverlayRoot : null;
            if (host == null) return;

            var go = new GameObject("ProfileSheet");
            go.transform.SetParent(host, false);
            var rt = go.AddComponent<RectTransform>();
            UiKit.Stretch(rt);
            go.AddComponent<ProfileSheet>().Build(rt);
            _open = go.GetComponent<ProfileSheet>();
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

        private void Build(RectTransform root)
        {
            Button scrim = UiKit.MakeButton(root, "Scrim", null, 0, new Color(0f, 0f, 0f, 0.5f),
                                            Color.white, Close);
            UiKit.Stretch(scrim.GetComponent<RectTransform>());

            Image card = UiKit.MakeRounded(root, "Card", CardBg, 18f);
            RectTransform crt = card.rectTransform;
            crt.anchorMin = new Vector2(0.5f, 0.5f);
            crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(
                Mathf.Min(UiKit.Css(360f), Screen.width / UiKit.CanvasScale - UiKit.Css(48f)), 0f);
            crt.anchoredPosition = Vector2.zero;

            float pad = UiKit.Css(20f);
            float y = pad;

            // 64 px circle with the first letter, centred (RN profBig).
            Image avatar = UiKit.MakeCircle(card.transform, "Avatar", AvatarBg);
            RectTransform art = avatar.rectTransform;
            art.anchorMin = new Vector2(0.5f, 1f);
            art.anchorMax = new Vector2(0.5f, 1f);
            art.pivot = new Vector2(0.5f, 1f);
            art.sizeDelta = new Vector2(UiKit.Css(64f), UiKit.Css(64f));
            art.anchoredPosition = new Vector2(0f, -y);

            Text initial = UiKit.MakeLine(avatar.transform, "Initial",
                                          Account.Initial(Account.Name, Account.Email),
                                          UiKit.CssFont(28f), TextAnchor.MiddleCenter, Color.white);
            initial.fontStyle = FontStyle.Bold;
            UiKit.Stretch(initial.rectTransform);
            y += UiKit.Css(64f) + UiKit.Css(10f);

            int nSize = UiKit.CssFont(17f);
            Text name = UiKit.MakeLine(card.transform, "Name",
                                       string.IsNullOrWhiteSpace(Account.Name)
                                           ? UiStrings.Tr("(ยังไม่ตั้งชื่อ)") : Account.Name,
                                       nSize, TextAnchor.UpperCenter, NameFg);
            name.fontStyle = FontStyle.Bold;
            Row(name.rectTransform, pad, y, UiKit.RowHeight(nSize));
            y += UiKit.LineHeight(nSize) + UiKit.Css(3f);

            int eSize = UiKit.CssFont(12f);
            Text mail = UiKit.MakeLine(card.transform, "Email", Account.Email, eSize,
                                       TextAnchor.UpperCenter, UiKit.TextDim);
            Row(mail.rectTransform, pad, y, UiKit.RowHeight(eSize));
            y += UiKit.LineHeight(eSize) + UiKit.Css(16f);

            float btnH = UiKit.Css(46f);
            float half = (crt.sizeDelta.x - pad * 2f - UiKit.Css(10f)) * 0.5f;

            Button close = UiKit.MakeButton(card.transform, "Close", UiStrings.Tr("ปิด"),
                                            UiKit.CssFont(14f), CancelBg, UiKit.TextMain, Close);
            Round(close);
            RectTransform clrt = close.GetComponent<RectTransform>();
            clrt.anchorMin = new Vector2(0f, 1f);
            clrt.anchorMax = new Vector2(0f, 1f);
            clrt.pivot = new Vector2(0f, 1f);
            clrt.sizeDelta = new Vector2(half, btnH);
            clrt.anchoredPosition = new Vector2(pad, -y);

            Button logout = UiKit.MakeButton(card.transform, "Logout", UiStrings.Tr("ออกจากระบบ"),
                                             UiKit.CssFont(14f), UiKit.Accent, UiKit.OnAccent, DoLogout);
            Round(logout);
            Text ll = logout.GetComponentInChildren<Text>();
            if (ll != null) ll.fontStyle = FontStyle.Bold;
            RectTransform lrt = logout.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(1f, 1f);
            lrt.anchorMax = new Vector2(1f, 1f);
            lrt.pivot = new Vector2(1f, 1f);
            lrt.sizeDelta = new Vector2(half, btnH);
            lrt.anchoredPosition = new Vector2(-pad, -y);
            y += btnH + UiKit.Css(14f);

            int dSize = UiKit.CssFont(13f);
            Button del = UiKit.MakeButton(card.transform, "Delete", UiStrings.Tr("ลบบัญชี"),
                                          dSize, new Color(0f, 0f, 0f, 0f), DangerFg, DoDelete);
            _delete = del.GetComponentInChildren<Text>();
            RectTransform drt = del.GetComponent<RectTransform>();
            drt.anchorMin = new Vector2(0.5f, 1f);
            drt.anchorMax = new Vector2(0.5f, 1f);
            drt.pivot = new Vector2(0.5f, 1f);
            drt.sizeDelta = new Vector2(crt.sizeDelta.x - pad * 2f, UiKit.RowHeight(dSize) + UiKit.Css(6f));
            drt.anchoredPosition = new Vector2(0f, -y);
            y += UiKit.RowHeight(dSize) + UiKit.Css(6f);

            crt.sizeDelta = new Vector2(crt.sizeDelta.x, y + pad);
        }

        private static void Round(Button b)
        {
            Image img = b.GetComponent<Image>();
            if (img == null) return;
            img.sprite = UiKit.RoundedSprite(13f);
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

        private void DoLogout()
        {
            StartCoroutine(AccountClient.Logout(() =>
            {
                Close();
                Toast.ShowTr("ออกจากระบบแล้ว");
                Changed?.Invoke();
            }));
        }

        /// <summary>
        /// Two taps, not one. The first arms the button and says what will happen; the second
        /// does it. A single red word that deletes an account on contact is a trap.
        /// </summary>
        private void DoDelete()
        {
            if (!_armed)
            {
                _armed = true;
                if (_delete != null) _delete.text = UiStrings.Tr("แตะอีกครั้งเพื่อลบบัญชีถาวร");
                return;
            }

            StartCoroutine(AccountClient.DeleteAccount(ok =>
            {
                Close();
                Toast.ShowTr(ok ? "ลบบัญชีแล้ว" : "ลบบัญชีไม่สำเร็จ");
                if (ok) Changed?.Invoke();
            }));
        }
    }
}
