using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace ARSpaceMemo
{
    public class MemoManager : MonoBehaviour
    {
        [SerializeField] private TMP_Text memoCountText;

        private readonly List<MemoCard> memoCards = new();
        private MemoCard selectedMemo;

        public MemoCard SelectedMemo => selectedMemo;

        public void Register(MemoCard memoCard)
        {
            if (memoCard == null || memoCards.Contains(memoCard))
            {
                return;
            }

            memoCards.Add(memoCard);
            UpdateCount();
        }

        public void Select(MemoCard memoCard)
        {
            if (selectedMemo == memoCard)
            {
                return;
            }

            if (selectedMemo != null)
            {
                selectedMemo.SetSelected(false);
            }

            selectedMemo = memoCard;

            if (selectedMemo != null)
            {
                selectedMemo.SetSelected(true);
            }

            UpdateCount();
        }

        public bool SaveSelectedFromInput(MemoInputController inputController)
        {
            if (selectedMemo == null || inputController == null)
            {
                return false;
            }

            selectedMemo.SetText(inputController.GetCurrentMemoText());
            return true;
        }

        public void DeleteSelected()
        {
            if (selectedMemo == null)
            {
                return;
            }

            MemoCard memoToDelete = selectedMemo;
            selectedMemo = null;
            memoCards.Remove(memoToDelete);

            if (memoToDelete != null)
            {
                Destroy(memoToDelete.gameObject);
            }

            UpdateCount();
        }

        public void ClearAll()
        {
            for (int i = memoCards.Count - 1; i >= 0; i--)
            {
                if (memoCards[i] != null)
                {
                    Destroy(memoCards[i].gameObject);
                }
            }

            memoCards.Clear();
            selectedMemo = null;
            UpdateCount();
        }

        public void SetCountText(TMP_Text countText)
        {
            memoCountText = countText;
            UpdateCount();
        }

        private void UpdateCount()
        {
            if (memoCountText != null)
            {
                memoCountText.text = selectedMemo == null
                    ? $"Memo {memoCards.Count}"
                    : $"Memo {memoCards.Count} | Selected";
            }
        }
    }
}
