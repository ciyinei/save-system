using UnityEngine;

namespace SaveSystem
{
    public interface ISerializable
    {
        void LoadData(GameData data);
        void SaveData(GameData data);
    }
}
