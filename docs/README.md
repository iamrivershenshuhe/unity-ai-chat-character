# Docs — GitHub Pages 報告參考頁

這個資料夾的內容會被 GitHub Pages serve 出去。

**Live URL（啟用後）：**
https://iamrivershenshuhe.github.io/unity-ai-chat-character/

## 一次性設定 — 啟用 GitHub Pages

1. 進 https://github.com/iamrivershenshuhe/unity-ai-chat-character/settings/pages
2. **Source** 選 `Deploy from a branch`
3. **Branch** 選 `main`，資料夾選 `/docs`
4. 按 **Save**
5. 等 30 秒 ~ 2 分鐘後，上面那個 URL 就會生效

## 更新內容

直接編輯 `docs/index.html`，commit + push 到 main，Pages 會自動重新部署（約 30 秒）。

```bash
git add docs/
git commit -m "docs: update showcase page"
git push origin main
```

## 檔案

- `index.html` — 單頁 showcase，含 4 zone 介紹、技術亮點、架構、開發歷程
- `README.md` — 這份說明
