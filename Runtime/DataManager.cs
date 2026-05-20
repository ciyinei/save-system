using UnityEngine;

namespace SaveSystem
{
    /// <summary>
    /// Manages saving and loading game data. Coordinates all <see cref="ISerializable"/> objects
    /// in the scene and delegates file I/O to <see cref="FileDataHandler{T}"/>.
    /// The caller is responsible for triggering saves on application pause and quit.
    /// </summary>
    public class DataManager<T> where T : SaveData, new()
    {
        private readonly string fileName;
        private readonly bool usePlayerPrefs;
        private readonly FileDataHandler<T> dataHandler;

        private T currentData;
        private List<ISerializable> serializableObjects = new();

        /// <summary>
        /// The current save data loaded in memory.
        /// </summary>
        public T CurrentData => currentData;

        public DataManager(string fileName = "saveData.json", bool usePlayerPrefs = false)
        {
            this.fileName = fileName;
            this.usePlayerPrefs = usePlayerPrefs;

            dataHandler = new FileDataHandler<T>(Application.persistentDataPath, this.fileName);
        }

        /// <summary>
        /// Resets save data to its default values.
        /// Called automatically when no saved data is found on load.
        /// </summary>
        public void NewGame()
        {
            currentData = new T();
        }

        /// <summary>
        /// Synchronously loads game data from disk and applies it to all
        /// <see cref="ISerializable"/> objects in the scene.
        /// Prefer <see cref="LoadGameAsync"/> to avoid blocking the main thread.
        /// </summary>
        public void LoadGame()
        {
            currentData = dataHandler.Load(usePlayerPrefs);

            if (currentData == null)
            {
                Debug.LogWarning("[DataManager] No save file found. Initializing to defaults.");
                NewGame();
            }

            serializableObjects = FindAllSerializableObjects();
            ApplyDataToObjects();
        }

        /// <summary>
        /// Synchronously collects data from all <see cref="ISerializable"/> objects
        /// and writes it to disk.
        /// Prefer <see cref="SaveGameAsync"/> to avoid blocking the main thread.
        /// </summary>
        /// <param name="clearDestroyed">
        /// If true, removes any destroyed objects from the serializable list before saving.
        /// </param>
        public void SaveGame(bool clearDestroyed = false)
        {
            serializableObjects = FindAllSerializableObjects();
            PrepareDataForSaving(clearDestroyed);
            dataHandler.Save(currentData, usePlayerPrefs);
        }

        /// <summary>
        /// Asynchronously loads game data from disk and applies it to all
        /// <see cref="ISerializable"/> objects in the scene.
        /// Data is applied on the main thread to allow safe access to Unity components.
        /// </summary>
        public async Task LoadGameAsync()
        {
            currentData = await dataHandler.LoadAsync(usePlayerPrefs);

            if (currentData == null)
            {
                Debug.LogWarning("[DataManager] No save file found. Initializing to defaults.");
                NewGame();
            }

            serializableObjects = FindAllSerializableObjects();
            ApplyDataToObjects();
        }

        /// <summary>
        /// Asynchronously collects data from all <see cref="ISerializable"/> objects
        /// and writes it to disk.
        /// </summary>
        /// <param name="clearDestroyed">
        /// If true, removes any destroyed objects from the serializable list before saving.
        /// </param>
        public async Task SaveGameAsync(bool clearDestroyed = false)
        {
            serializableObjects = FindAllSerializableObjects();
            PrepareDataForSaving(clearDestroyed);
            await dataHandler.SaveAsync(currentData, usePlayerPrefs);
        }

        /// <summary>
        /// Calls <see cref="ISerializable.LoadData"/> on all registered serializable objects,
        /// applying the current <see cref="CurrentData"/> state to each.
        /// </summary>
        private void ApplyDataToObjects()
        {
            foreach (var saveable in serializableObjects)
            {
                saveable.LoadData(currentData);
            }
        }

        /// <summary>
        /// Calls <see cref="ISerializable.SaveData"/> on all registered serializable objects,
        /// collecting their state into <see cref="CurrentData"/> before writing to disk.
        /// </summary>
        /// <param name="clearDestroyed">
        /// If true, purges destroyed objects from the list before collecting data.
        /// </param>
        private void PrepareDataForSaving(bool clearDestroyed)
        {
            if (clearDestroyed)
            {
                CleanupDestroyedObjects();
            }

            foreach (var serializable in serializableObjects)
            {
                serializable.SaveData(currentData);
            }
        }

        /// <summary>
        /// Manually registers an <see cref="ISerializable"/> object with the manager.
        /// Use this for objects that are spawned at runtime and may not be found by
        /// <see cref="FindAllSerializableObjects"/>.
        /// </summary>
        public void AddSerializableObject(ISerializable serializable) => serializableObjects.Add(serializable);

        /// <summary>
        /// Scans the scene for all <see cref="MonoBehaviour"/> instances that implement
        /// <see cref="ISerializable"/>, including inactive objects.
        /// Note: this class is not a MonoBehaviour, but still depends on Unity's scene
        /// via <see cref="Object.FindObjectsByType{T}"/>. Ensure it is only instantiated
        /// in a Unity context.
        /// </summary>
        private List<ISerializable> FindAllSerializableObjects()
        {
            var serializables = Object.FindObjectsByType<MonoBehaviour>(
                            FindObjectsInactive.Include,
                            FindObjectsSortMode.InstanceID)
                    .OfType<ISerializable>()
                    .ToList();

            Debug.Log($"[DataManager] Found {serializables.Count} serializable objects.");

            return serializables;
        }

        /// <summary>
        /// Removes null or destroyed entries from the serializable objects list.
        /// </summary>
        private void CleanupDestroyedObjects()
        {
            serializableObjects.RemoveAll(obj => obj == null || (obj as MonoBehaviour) == null);
        }
    }
}