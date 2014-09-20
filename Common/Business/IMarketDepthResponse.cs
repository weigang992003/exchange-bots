using System.Collections.Generic;


namespace Common.Business
{
    public interface IMarketDepthResponse<T> where T : IMarketOrder
    {
        List<T> Bids { get; } 
        List<T> Asks { get; }
    }
}
