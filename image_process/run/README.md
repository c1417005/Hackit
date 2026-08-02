# image_process/run

このフォルダはテスト実行や一時ファイル保存用の仮フォルダです。

使い方:
- `image_process/color_extraction.py` や `image_process/image_Processing.py` を実行するときに、出力先や一時ファイルをこのフォルダに設定できます。
- 例:
    - `python ..\color_extraction.py ..\IMG_4411_sword.png`
    - `python ..\image_Processing.py ..\IMG_4411.jpg`

## 実行ラッパー

`run.py` から以下のように実行できます:

```powershell
cd C:\Users\kanata\Hackit\image_process\run
python run.py color_extraction ..\IMG_4411_sword.png
python run.py image_processing ..\IMG_4411.jpg
python run.py color_extraction image_process\IMG_4411.jpg
```

`run.py` は、`image_process` フォルダ内のスクリプトを `sys.executable` で直接呼び出します。

### 背景除去済み画像の自動選択

指定した画像ファイルの隣に以下のような背景除去済み画像があれば、`run.py` が自動的にそちらを優先して実行します:

- `*_no_bg.png`
- `*_sword.png`
- `*_sword_sword.png`

たとえば `IMG_4411.jpg` を指定したとき、`IMG_4411_sword.png` が存在すればそちらが使われます。

注意:
- 実行フォルダではなく、ソースフォルダは `image_process/` です。
- 実行結果や生成ファイルは必要に応じて `run/` に保存してください。
