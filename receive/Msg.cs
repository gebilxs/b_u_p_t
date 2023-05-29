using Newtonsoft.Json;
using System;
using UnityEngine;

[Serializable]
public class Msg
{
    public uint Id;
    public string Content;

    public Msg()
    {
        Id = 0;
        Content = null;
    }

    public Msg(uint id, string con)
    {
        Id = id;
        Content = con;
    }

    public Msg(uint id, object con)
    {
        Id = id;
        Content = JsonConvert.SerializeObject(con); //JsonUtility.ToJson(con); JsonUtility转部分类会转出空json，所以弃用
    }

    public override string ToString()
    {
        return JsonConvert.SerializeObject(this);
    }

    public static Msg Parse(string msgContent)
    {
        return JsonConvert.DeserializeObject<Msg>(msgContent);
    }
}