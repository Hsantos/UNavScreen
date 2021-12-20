using UnityEngine;

namespace Elselam.UnityRouter.Domain
{
    /// <summary>
    /// Domain used to recognize deep links
    /// </summary>
    [CreateAssetMenu(fileName = "UrlDomain", menuName = "Elselam/UNavScreen/UrlDomain")]
    public class AppUrlDomain : ScriptableObject, IUrlDomainProvider
    {
        [SerializeField] private string url = "domain://";
        public string Url => url;
    }
}