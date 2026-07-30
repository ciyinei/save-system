using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
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
        readonly string dirPath;
        readonly string fileName;
        readonly bool usePlayerPrefs;
        readonly FileDataHandler<T> dataHandler;
        readonly HashSet<ISerializable> serializableObjects = new();

        T currentData;

        /// <summary>
        /// The current save data loaded in memory.
        /// </summary>
        public T CurrentData => currentData;

        public DataManager(string dirPath, string fileName = "saveData.json", bool usePlayerPrefs = false)
        {
            this.dirPath = dirPath;
            this.fileName = fileName;
            this.usePlayerPrefs = usePlayerPrefs;

            dataHandler = new FileDataHandler<T>(this.dirPath, this.fileName);
        }

        /// <summary>
        /// Resets save data to its default values.
        /// Called automatically when no saved data is found on load.
        /// </summary>
        public void NewFileSave()
        {
            currentData = new T();
            serializableObjects.Clear();
        }

        /// <summary>
        /// Synchronously loads game data from disk or PlayerPrefs and applies it to all
        /// <see cref="ISerializable"/> objects in the scene.
        /// Prefer <see cref="LoadAsync"/> to avoid blocking the main thread.
        /// </summary>
        public void Load(bool scanScene = false)
        {
            currentData = dataHandler.Load(usePlayerPrefs);

            if (currentData == null)
            {
                Debug.LogWarning("[DataManager] No save file found. Initializing to defaults.");
                NewFileSave();
            }

            if (scanScene)
            {
                FindAllSerializableObjects();
            }
            
            ApplyDataToObjects();
        }

        /// <summary>
        /// Synchronously collects data from all <see cref="ISerializable"/> objects
        /// and writes it to disk or PlayerPrefs.
        /// Prefer <see cref="SaveAsync"/> to avoid blocking the main thread.
        /// </summary>
        /// <param name="scanScene">
        /// If true, scans the current scene before saving.
        /// </param>
        /// <param name="clearDestroyed">
        /// If true, removes any destroyed objects from the serializable list before saving.
        /// </param>
        public void Save(bool scanScene = false, bool clearDestroyed = false)
        {
            EnsureCurrentData();
        
            if (scanScene)
            {
                FindAllSerializableObjects();
            }
            
            PrepareDataForSaving(clearDestroyed);
            dataHandler.Save(currentData, usePlayerPrefs);
        }

        /// <summary>
        /// Asynchronously loads game data from disk or PlayerPrefs and applies it to all
        /// <see cref="ISerializable"/> objects in the scene.
        /// Data is applied on the main thread after loading completes.
        /// </summary>
        public async Task LoadAsync(bool scanScene = false)
        {
            currentData = await dataHandler.LoadAsync(usePlayerPrefs);

            if (currentData == null)
            {
                Debug.LogWarning($"[DataManager] No save file found at {dirPath}. Initializing to defaults.");
                NewFileSave();
            }

            if (scanScene)
            {
                FindAllSerializableObjects();
            }
            
            ApplyDataToObjects();
        }

        /// <summary>
        /// Asynchronously collects data from all <see cref="ISerializable"/> objects
        /// and writes it to disk or PlayerPrefs.
        /// </summary>
        /// <param name="scanScene">
        /// If true, scans the current scene before saving.
        /// </param>
        /// <param name="clearDestroyed">
        /// If true, removes any destroyed objects from the serializable list before saving.
        /// </param>
        public async Task SaveAsync(bool scanScene = false, bool clearDestroyed = false)
        {
            EnsureCurrentData();
        
            if (scanScene)
            {
                FindAllSerializableObjects();
            }
            
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
                saveable?.LoadData(currentData);
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
                serializable?.SaveData(currentData);
            }
        }

        /// <summary>
        /// Ensures save data exists before collecting or writing data.
        /// </summary>
        private void EnsureCurrentData()
        {
            currentData ??= new T();
        }

        /// <summary>
        /// Manually registers an <see cref="ISerializable"/> object with the manager.
        /// Use this for objects that are spawned at runtime and may not be found by
        /// <see cref="FindAllSerializableObjects"/>.
        /// </summary>
        public void AddSerializableObject(ISerializable serializable)
        {
            if (serializable != null)
            {
                serializableObjects.Add(serializable);
            }
        }
        
        /// <summary>
        /// Manually unregisters an <see cref="ISerializable"/> object from the manager.
        /// </summary>
        public void RemoveSerializableObject(ISerializable serializable)
        {
            if (serializable != null)
            {
                serializableObjects.Remove(serializable);
            }
        }

        /// <summary>
        /// Scans the scene for all <see cref="MonoBehaviour"/> instances that implement
        /// <see cref="ISerializable"/>, including inactive objects.
        /// Note: this class is not a MonoBehaviour, but still depends on Unity's scene
        /// via <see cref="Object.FindObjectsByType{T}"/>. Ensure it is only instantiated
        /// in a Unity context.
        /// </summary>
        private void FindAllSerializableObjects()
        {
            var serializables = Object.FindObjectsByType<MonoBehaviour>(
                            FindObjectsInactive.Include,
                            FindObjectsSortMode.InstanceID)
                    .OfType<ISerializable>();

            serializableObjects.UnionWith(serializables);
            
            Debug.Log($"[DataManager] Found {serializables.Length} serializable objects.");
        }

        /// <summary>
        /// Removes null or destroyed entries from the serializable objects list.
        /// </summary>
        private void CleanupDestroyedObjects()
        {
            serializableObjects.RemoveWhere(obj => obj == null || obj is MonoBehaviour mb && mb == null);
        }
    }
}