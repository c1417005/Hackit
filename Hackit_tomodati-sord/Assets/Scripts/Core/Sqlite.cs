using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// SQLite の最小ラッパー。読み取りと単純な書き込みだけできれば良い、という割り切り。
///
/// ネイティブは2箇所から探す:
///   1. "sqlite3"     … Assets/Plugins/x86_64/sqlite3.dll を置いた場合はこちらが使われる
///   2. "winsqlite3"  … Windows 10以降が標準で持っているもの。何も置かなくても動く
///
/// 1 が見つかればそちらを優先するので、あとから公式の sqlite3.dll を置けば自動で乗り換わる。
/// winsqlite3 は Windows 専用なので、他プラットフォームに出すときは sqlite3.dll を用意すること。
/// </summary>
public sealed class Sqlite : IDisposable
{
    const int SQLITE_OK = 0;
    const int SQLITE_ROW = 100;
    const int SQLITE_DONE = 101;
    const int SQLITE_BLOB = 4;

    const int OPEN_READONLY = 0x00000001;
    const int OPEN_READWRITE = 0x00000002;

    IntPtr _db;

    /// <summary>ネイティブが見つかったか。false ならこの環境では使えない。</summary>
    public static bool IsAvailable
    {
        get
        {
            EnsureProbed();
            return _backend != Backend.None;
        }
    }

    public static string BackendName
    {
        get
        {
            EnsureProbed();
            return _backend.ToString();
        }
    }

    /// <summary>読み書きで開く。開けなければ読み取り専用で再挑戦する。</summary>
    public Sqlite(string path)
    {
        EnsureProbed();
        if (_backend == Backend.None)
        {
            throw new DllNotFoundException("SQLite のネイティブライブラリが見つからない");
        }

        byte[] utf8Path = Utf8(path);

        int rc = Open(utf8Path, out _db, OPEN_READWRITE);
        if (rc != SQLITE_OK)
        {
            // 読み取り専用の場所に置かれている場合もあるので、そこは許容する
            Close(_db);
            _db = IntPtr.Zero;
            rc = Open(utf8Path, out _db, OPEN_READONLY);
        }

        if (rc != SQLITE_OK)
        {
            string message = _db != IntPtr.Zero ? ErrorMessage(_db) : "rc=" + rc;
            Close(_db);
            _db = IntPtr.Zero;
            throw new InvalidOperationException($"SQLite を開けない: {path} ({message})");
        }
    }

    /// <summary>1行ぶんの値。列名で引く。</summary>
    public sealed class Row
    {
        readonly Dictionary<string, string> _values = new Dictionary<string, string>();
        readonly Dictionary<string, byte[]> _blobs = new Dictionary<string, byte[]>();

        internal void Set(string column, string value) => _values[column] = value;
        internal void SetBlob(string column, byte[] value) => _blobs[column] = value;

        /// <summary>画像などのバイナリ列。無ければ null。</summary>
        public byte[] GetBlob(string column)
        {
            return _blobs.TryGetValue(column, out byte[] v) ? v : null;
        }

        public string GetString(string column, string fallback = "")
        {
            return _values.TryGetValue(column, out string v) && v != null ? v : fallback;
        }

        public int GetInt(string column, int fallback = 0)
        {
            return int.TryParse(GetString(column), out int v) ? v : fallback;
        }

        public float GetFloat(string column, float fallback = 0f)
        {
            return float.TryParse(GetString(column),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out float v) ? v : fallback;
        }
    }

    /// <summary>SELECT を投げて全行返す。件数が知れているテーブル向け。</summary>
    public List<Row> Query(string sql, params object[] parameters)
    {
        var rows = new List<Row>();
        IntPtr statement = Prepare(sql, parameters);

        try
        {
            int columnCount = ColumnCount(statement);

            var names = new string[columnCount];
            for (int i = 0; i < columnCount; i++)
            {
                names[i] = FromUtf8(ColumnName(statement, i));
            }

            while (Step(statement) == SQLITE_ROW)
            {
                var row = new Row();
                for (int i = 0; i < columnCount; i++)
                {
                    if (ColumnType(statement, i) == SQLITE_BLOB)
                    {
                        row.SetBlob(names[i], ReadBlob(statement, i));
                        continue;
                    }
                    row.Set(names[i], FromUtf8(ColumnText(statement, i)));
                }
                rows.Add(row);
            }
        }
        finally
        {
            Finalize(statement);
        }

        return rows;
    }

    /// <summary>INSERT / UPDATE / CREATE など、行を返さない文。</summary>
    public void Execute(string sql, params object[] parameters)
    {
        IntPtr statement = Prepare(sql, parameters);

        try
        {
            int rc = Step(statement);
            if (rc != SQLITE_DONE && rc != SQLITE_ROW)
            {
                throw new InvalidOperationException($"SQL の実行に失敗: {ErrorMessage(_db)}");
            }
        }
        finally
        {
            Finalize(statement);
        }
    }

    IntPtr Prepare(string sql, object[] parameters)
    {
        byte[] utf8 = Utf8(sql);
        int rc = PrepareV2(_db, utf8, utf8.Length - 1, out IntPtr statement, IntPtr.Zero);

        if (rc != SQLITE_OK || statement == IntPtr.Zero)
        {
            throw new InvalidOperationException($"SQL を準備できない: {ErrorMessage(_db)}\n{sql}");
        }

        // ? を左から順に埋める
        if (parameters != null)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                object p = parameters[i];
                int index = i + 1;

                switch (p)
                {
                    case null: BindNull(statement, index); break;
                    case int n: BindInt(statement, index, n); break;
                    case long n: BindInt64(statement, index, n); break;
                    case float f: BindDouble(statement, index, f); break;
                    case double d: BindDouble(statement, index, d); break;
                    default:
                        byte[] text = Utf8(p.ToString());
                        BindText(statement, index, text, text.Length - 1, new IntPtr(-1));
                        break;
                }
            }
        }

        return statement;
    }

    public void Dispose()
    {
        if (_db != IntPtr.Zero)
        {
            Close(_db);
            _db = IntPtr.Zero;
        }
    }

    // ---------- 文字列の受け渡し ----------

    static byte[] Utf8(string value)
    {
        // ネイティブ側は null 終端を期待するので1バイト足す
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        var terminated = new byte[bytes.Length + 1];
        Buffer.BlockCopy(bytes, 0, terminated, 0, bytes.Length);
        return terminated;
    }

    /// <summary>
    /// Marshal.PtrToStringUTF8 は環境によって無い場合があるので自前で読む。
    /// </summary>
    static string FromUtf8(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero) return null;

        int length = 0;
        while (Marshal.ReadByte(pointer, length) != 0) length++;
        if (length == 0) return string.Empty;

        var buffer = new byte[length];
        Marshal.Copy(pointer, buffer, 0, length);
        return Encoding.UTF8.GetString(buffer);
    }

    // ---------- ネイティブの選択 ----------

    enum Backend { Unprobed, None, Sqlite3, WinSqlite3 }

    static Backend _backend = Backend.Unprobed;

    static void EnsureProbed()
    {
        if (_backend != Backend.Unprobed) return;

        try
        {
            Native.LibVersionA();
            _backend = Backend.Sqlite3;
            return;
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }

        try
        {
            Native.LibVersionB();
            _backend = Backend.WinSqlite3;
            return;
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }

        _backend = Backend.None;
    }

    static bool UseSystem => _backend == Backend.WinSqlite3;

    static int Open(byte[] path, out IntPtr db, int flags) =>
        UseSystem ? Native.OpenB(path, out db, flags, IntPtr.Zero)
                  : Native.OpenA(path, out db, flags, IntPtr.Zero);

    static int Close(IntPtr db) =>
        db == IntPtr.Zero ? SQLITE_OK : (UseSystem ? Native.CloseB(db) : Native.CloseA(db));

    static int PrepareV2(IntPtr db, byte[] sql, int n, out IntPtr stmt, IntPtr tail) =>
        UseSystem ? Native.PrepareB(db, sql, n, out stmt, tail)
                  : Native.PrepareA(db, sql, n, out stmt, tail);

    static int Step(IntPtr stmt) => UseSystem ? Native.StepB(stmt) : Native.StepA(stmt);
    static int Finalize(IntPtr stmt) => UseSystem ? Native.FinalizeB(stmt) : Native.FinalizeA(stmt);
    static int ColumnCount(IntPtr stmt) => UseSystem ? Native.ColumnCountB(stmt) : Native.ColumnCountA(stmt);
    static IntPtr ColumnName(IntPtr stmt, int i) => UseSystem ? Native.ColumnNameB(stmt, i) : Native.ColumnNameA(stmt, i);
    static IntPtr ColumnText(IntPtr stmt, int i) => UseSystem ? Native.ColumnTextB(stmt, i) : Native.ColumnTextA(stmt, i);
    static int ColumnType(IntPtr stmt, int i) => UseSystem ? Native.ColumnTypeB(stmt, i) : Native.ColumnTypeA(stmt, i);

    /// <summary>BLOB 列をバイト配列で読む。ポインタは step するまでしか有効でないのでその場でコピーする。</summary>
    static byte[] ReadBlob(IntPtr stmt, int index)
    {
        int length = UseSystem ? Native.ColumnBytesB(stmt, index) : Native.ColumnBytesA(stmt, index);
        if (length <= 0) return null;

        IntPtr pointer = UseSystem ? Native.ColumnBlobB(stmt, index) : Native.ColumnBlobA(stmt, index);
        if (pointer == IntPtr.Zero) return null;

        var buffer = new byte[length];
        Marshal.Copy(pointer, buffer, 0, length);
        return buffer;
    }

    static int BindNull(IntPtr s, int i) => UseSystem ? Native.BindNullB(s, i) : Native.BindNullA(s, i);
    static int BindInt(IntPtr s, int i, int v) => UseSystem ? Native.BindIntB(s, i, v) : Native.BindIntA(s, i, v);
    static int BindInt64(IntPtr s, int i, long v) => UseSystem ? Native.BindInt64B(s, i, v) : Native.BindInt64A(s, i, v);
    static int BindDouble(IntPtr s, int i, double v) => UseSystem ? Native.BindDoubleB(s, i, v) : Native.BindDoubleA(s, i, v);
    static int BindText(IntPtr s, int i, byte[] v, int n, IntPtr free) =>
        UseSystem ? Native.BindTextB(s, i, v, n, free) : Native.BindTextA(s, i, v, n, free);

    static string ErrorMessage(IntPtr db) =>
        FromUtf8(UseSystem ? Native.ErrMsgB(db) : Native.ErrMsgA(db)) ?? "unknown error";

    /// <summary>
    /// DllImport は名前を定数でしか書けないので、同じ関数を2組ぶん宣言している。
    /// A = "sqlite3"（自前で置いた場合）、B = "winsqlite3"（Windows標準）。
    /// </summary>
    static class Native
    {
        const string A = "sqlite3";
        const string B = "winsqlite3";
        const CallingConvention Cdecl = CallingConvention.Cdecl;

        [DllImport(A, EntryPoint = "sqlite3_libversion", CallingConvention = Cdecl)]
        internal static extern IntPtr LibVersionA();
        [DllImport(B, EntryPoint = "sqlite3_libversion", CallingConvention = Cdecl)]
        internal static extern IntPtr LibVersionB();

        [DllImport(A, EntryPoint = "sqlite3_open_v2", CallingConvention = Cdecl)]
        internal static extern int OpenA(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
        [DllImport(B, EntryPoint = "sqlite3_open_v2", CallingConvention = Cdecl)]
        internal static extern int OpenB(byte[] filename, out IntPtr db, int flags, IntPtr vfs);

        [DllImport(A, EntryPoint = "sqlite3_close", CallingConvention = Cdecl)]
        internal static extern int CloseA(IntPtr db);
        [DllImport(B, EntryPoint = "sqlite3_close", CallingConvention = Cdecl)]
        internal static extern int CloseB(IntPtr db);

        [DllImport(A, EntryPoint = "sqlite3_prepare_v2", CallingConvention = Cdecl)]
        internal static extern int PrepareA(IntPtr db, byte[] sql, int nBytes, out IntPtr stmt, IntPtr tail);
        [DllImport(B, EntryPoint = "sqlite3_prepare_v2", CallingConvention = Cdecl)]
        internal static extern int PrepareB(IntPtr db, byte[] sql, int nBytes, out IntPtr stmt, IntPtr tail);

        [DllImport(A, EntryPoint = "sqlite3_step", CallingConvention = Cdecl)]
        internal static extern int StepA(IntPtr stmt);
        [DllImport(B, EntryPoint = "sqlite3_step", CallingConvention = Cdecl)]
        internal static extern int StepB(IntPtr stmt);

        [DllImport(A, EntryPoint = "sqlite3_finalize", CallingConvention = Cdecl)]
        internal static extern int FinalizeA(IntPtr stmt);
        [DllImport(B, EntryPoint = "sqlite3_finalize", CallingConvention = Cdecl)]
        internal static extern int FinalizeB(IntPtr stmt);

        [DllImport(A, EntryPoint = "sqlite3_column_count", CallingConvention = Cdecl)]
        internal static extern int ColumnCountA(IntPtr stmt);
        [DllImport(B, EntryPoint = "sqlite3_column_count", CallingConvention = Cdecl)]
        internal static extern int ColumnCountB(IntPtr stmt);

        [DllImport(A, EntryPoint = "sqlite3_column_name", CallingConvention = Cdecl)]
        internal static extern IntPtr ColumnNameA(IntPtr stmt, int index);
        [DllImport(B, EntryPoint = "sqlite3_column_name", CallingConvention = Cdecl)]
        internal static extern IntPtr ColumnNameB(IntPtr stmt, int index);

        [DllImport(A, EntryPoint = "sqlite3_column_text", CallingConvention = Cdecl)]
        internal static extern IntPtr ColumnTextA(IntPtr stmt, int index);
        [DllImport(B, EntryPoint = "sqlite3_column_text", CallingConvention = Cdecl)]
        internal static extern IntPtr ColumnTextB(IntPtr stmt, int index);

        [DllImport(A, EntryPoint = "sqlite3_column_type", CallingConvention = Cdecl)]
        internal static extern int ColumnTypeA(IntPtr stmt, int index);
        [DllImport(B, EntryPoint = "sqlite3_column_type", CallingConvention = Cdecl)]
        internal static extern int ColumnTypeB(IntPtr stmt, int index);

        [DllImport(A, EntryPoint = "sqlite3_column_blob", CallingConvention = Cdecl)]
        internal static extern IntPtr ColumnBlobA(IntPtr stmt, int index);
        [DllImport(B, EntryPoint = "sqlite3_column_blob", CallingConvention = Cdecl)]
        internal static extern IntPtr ColumnBlobB(IntPtr stmt, int index);

        [DllImport(A, EntryPoint = "sqlite3_column_bytes", CallingConvention = Cdecl)]
        internal static extern int ColumnBytesA(IntPtr stmt, int index);
        [DllImport(B, EntryPoint = "sqlite3_column_bytes", CallingConvention = Cdecl)]
        internal static extern int ColumnBytesB(IntPtr stmt, int index);

        [DllImport(A, EntryPoint = "sqlite3_bind_null", CallingConvention = Cdecl)]
        internal static extern int BindNullA(IntPtr stmt, int index);
        [DllImport(B, EntryPoint = "sqlite3_bind_null", CallingConvention = Cdecl)]
        internal static extern int BindNullB(IntPtr stmt, int index);

        [DllImport(A, EntryPoint = "sqlite3_bind_int", CallingConvention = Cdecl)]
        internal static extern int BindIntA(IntPtr stmt, int index, int value);
        [DllImport(B, EntryPoint = "sqlite3_bind_int", CallingConvention = Cdecl)]
        internal static extern int BindIntB(IntPtr stmt, int index, int value);

        [DllImport(A, EntryPoint = "sqlite3_bind_int64", CallingConvention = Cdecl)]
        internal static extern int BindInt64A(IntPtr stmt, int index, long value);
        [DllImport(B, EntryPoint = "sqlite3_bind_int64", CallingConvention = Cdecl)]
        internal static extern int BindInt64B(IntPtr stmt, int index, long value);

        [DllImport(A, EntryPoint = "sqlite3_bind_double", CallingConvention = Cdecl)]
        internal static extern int BindDoubleA(IntPtr stmt, int index, double value);
        [DllImport(B, EntryPoint = "sqlite3_bind_double", CallingConvention = Cdecl)]
        internal static extern int BindDoubleB(IntPtr stmt, int index, double value);

        [DllImport(A, EntryPoint = "sqlite3_bind_text", CallingConvention = Cdecl)]
        internal static extern int BindTextA(IntPtr stmt, int index, byte[] value, int n, IntPtr free);
        [DllImport(B, EntryPoint = "sqlite3_bind_text", CallingConvention = Cdecl)]
        internal static extern int BindTextB(IntPtr stmt, int index, byte[] value, int n, IntPtr free);

        [DllImport(A, EntryPoint = "sqlite3_errmsg", CallingConvention = Cdecl)]
        internal static extern IntPtr ErrMsgA(IntPtr db);
        [DllImport(B, EntryPoint = "sqlite3_errmsg", CallingConvention = Cdecl)]
        internal static extern IntPtr ErrMsgB(IntPtr db);
    }
}
