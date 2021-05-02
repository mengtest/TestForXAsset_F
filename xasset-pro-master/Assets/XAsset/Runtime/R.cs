namespace libx {
    
    public class R {
        // prefab 加后缀名
        public static string GetPrefab(string name) {
            return string.Format("{0}.prefab", name);
        }

        // 场景 加后缀名
        public static string GetScene(string name) {
            return string.Format("{0}.unity", name);
        }
    }
}