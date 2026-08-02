using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

// FastAPI の /dbtest が返す JSON 1件分
// JsonUtility はフィールド名で対応づけるので、サーバー側のキーと同じ名前にする
[Serializable]
public class Person
{
    public int id;
    public string name;
    public float height;
    public float speed;
    public float attack;
}

// JsonUtility はトップレベルの配列を読めないため、
// サーバーは {"persons": [...]} の形で返している
[Serializable]
public class PersonList
{
    public Person[] persons;
}

public class DbTestClient : MonoBehaviour
{
    // 実機で試すときは PC の LAN IP に変える (例: http://192.168.1.10:8000)
    [SerializeField] private string baseUrl = "http://127.0.0.1:8000";

    private void Start()
    {
        StartCoroutine(FetchPersons());
    }

    private IEnumerator FetchPersons()
    {
        using (UnityWebRequest req = UnityWebRequest.Get(baseUrl + "/dbtest"))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"DB接続テスト失敗: {req.error}");
                yield break;
            }

            PersonList list = JsonUtility.FromJson<PersonList>(req.downloadHandler.text);

            Debug.Log($"DB接続テスト成功: {list.persons.Length} 件取得");
            foreach (Person p in list.persons)
            {
                Debug.Log($"[{p.id}] {p.name} 身長:{p.height} 速さ:{p.speed} 攻撃:{p.attack}");
            }
        }
    }
}
