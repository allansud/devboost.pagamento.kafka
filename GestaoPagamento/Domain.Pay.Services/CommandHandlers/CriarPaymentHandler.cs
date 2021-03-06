﻿using AutoMapper;
using Domain.Pay.Core;
using Domain.Pay.Core.Validador;
using Domain.Pay.Entities;
using Domain.Pay.Services.CommandHandlers.Interfaces;
using Domain.Pay.Services.Commands.Payments;
using Integration.Pay.Dto;
using Integration.Pay.Interfaces;
using Repository.Pay.UnitOfWork;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Domain.Pay.Services.CommandHandlers
{
    public class CriarPaymentHandler : ValidadorResponse, ICriarPaymentHandler
    {
        readonly IUnitOfWork _unitOfWork;
        readonly IMapper _mapper;
        readonly IPayAtOperatorService _payAtOperatorService;        
        readonly ISendPaymentKafka _sendPayToKafka;

        public CriarPaymentHandler(IUnitOfWork unitOfWork, IMapper mapper, IPayAtOperatorService payAtOperatorService, ISendPaymentKafka sendPayToKafka)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _payAtOperatorService = payAtOperatorService;            
            _sendPayToKafka = sendPayToKafka;
        }

        public async Task<ResponseResult> Handle(CriarPaymentCommand request)
        {
            // Registra a operação de pagamento
            var result = await AddDataBase(request);
            if (!result)
                return _response;
            // Chama MockAPI para tratar pagamento
            await CallMockApi(request);
            // Chama  WebHook retornando o status do pagamento
            request.Status = 2;
            await SendPaymentToKafka(request);
            // retorna a operação para Controller
            return _response;
        }

        async Task<bool> AddDataBase(CriarPaymentCommand request)
        {
            request.Validar();
            if (request.Notifications.Any())
            {
                _response.AddNotifications(request.Notifications);
                return false;
            }
            // Armazena informação da transação de pagamento
            var payment = _mapper.Map<Payment>(request);

            //Payment payment = new Payment(request.PayId, DateTime.Now, request.Name, request.Bandeira, request.NumeroCartao, request.Vencimento,
            //    request.CodigoSeguranca, request.Valor, request.Status);

            await _unitOfWork.PaymentRepository.InsertAsync(payment);
            await _unitOfWork.CommitAsync();
            return true;
        }

        async Task CallMockApi(CriarPaymentCommand request)
        {
            await _payAtOperatorService.ValidadePayAtOperator(new PayOperatorFilterDto()
            {
                Id = request.PayId,
                CreatedAt = DateTime.Now,
                Name = request.Name,
                Bandeira = request.Bandeira,
                NumeroCartao = request.NumeroCartao,
                Vencimento = request.Vencimento,
                CodigoSeguranca = request.CodigoSeguranca,
                Valor = (decimal)request.Valor,
                Status = "Envio"
            });
        }

        async Task SendPaymentToKafka(CriarPaymentCommand request)
        {
            var requestPaymentDto = new RequestPaymentDto
            {
                PayId = request.PayId,
                CreatedAt = request.CreatedAt,
                Name = request.Name,
                Bandeira = request.Bandeira,
                NumeroCartao = request.NumeroCartao,
                Vencimento = request.Vencimento,
                CodigoSeguranca = request.CodigoSeguranca,
                Valor = (decimal)request.Valor,
                Status = request.Status
            };
            var result = await _sendPayToKafka.SendPay(requestPaymentDto);
            if (result.Status == Confluent.Kafka.PersistenceStatus.NotPersisted)
            {
                request.AddNotification("", result.Message.Value);
                _response.AddNotifications(request.Notifications);
            }
        }
    }
}
