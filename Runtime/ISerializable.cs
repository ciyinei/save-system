using UnityEngine;

namespace SaveSystem
{
    public interface ISerializable
    {
        void LoadData(SaveData data);
        void SaveData(SaveData data);
    }
}
