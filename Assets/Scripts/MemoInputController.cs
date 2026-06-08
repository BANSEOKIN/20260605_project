using TMPro;
using UnityEngine;

namespace ARSpaceMemo
{
    public class MemoInputController : MonoBehaviour
    {
        [SerializeField] private TMP_InputField memoInputField;
        [SerializeField] private string defaultMemoText = "Memo";

        public string GetCurrentMemoText()
        {
            if (memoInputField == null)
            {
                return defaultMemoText;
            }

            string input = memoInputField.text;
            return string.IsNullOrWhiteSpace(input) ? defaultMemoText : input.Trim();
        }

        public void SetCurrentMemoText(string text)
        {
            if (memoInputField != null)
            {
                memoInputField.text = text ?? string.Empty;
            }
        }

        public void Clear()
        {
            SetCurrentMemoText(string.Empty);
        }

        public void SetInputField(TMP_InputField inputField)
        {
            memoInputField = inputField;
        }
    }
}
