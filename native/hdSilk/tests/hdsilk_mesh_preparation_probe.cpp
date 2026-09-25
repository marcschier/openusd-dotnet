// Copyright (c) marcschier. Licensed under the MIT License.

#include "../src/meshPreparation.h"

#include <array>
#include <iostream>
#include <string>
#include <thread>

namespace
{
void Require(bool value, const char* message)
{
    if (!value) { throw std::runtime_error(message); }
}

void ReservationArithmeticIsExactAndRejectsOverflow()
{
    HdSilkMeshPreparationPlan plan(4, 2);
    Require(plan.bytes == 372, "The coarse numeric buffer reservation changed.");
    plan.Attribute(1, 3, true);
    plan.Attribute(4, 2, false);
    Require(plan.bytes == 596, "The attribute overlap reservation changed.");
    const auto expectOverflow = [](auto action)
    {
        bool failed = false;
        try { action(); }
        catch (const std::overflow_error&) { failed = true; }
        Require(failed, "Reservation arithmetic silently wrapped.");
    };
    expectOverflow([] { (void)HdSilkMeshPreparationPlan(1, UINT64_MAX); });
    expectOverflow([] { (void)HdSilkMeshPreparationPlan(UINT64_MAX, 0); });
    expectOverflow([&] { plan.Attribute(UINT64_MAX, 4, false); });
    Require(plan.bytes == 596, "Failed accounting changed the existing reservation.");
}

void LimitsAndSharedOwnersPreserveExactBoundaries()
{
    const auto budget = std::make_shared<HdSilkMeshPreparationBudget>();
    Require(!budget->Acquire(100), "Legacy preparation unexpectedly acquired a budget lease.");
    budget->Configure(12);
    auto first = budget->Acquire(4);
    auto second = budget->Acquire(8);
    auto copy = second;
    Require(budget->Reserved() == 12 && budget->Peak() == 12, "Shared owners were counted twice.");
    bool failed = false;
    try { (void)budget->Acquire(1); }
    catch (const HdSilkMeshPreparationExceeded&) { failed = true; }
    Require(failed && budget->Reserved() == 12, "One byte over the limit was admitted or changed accounting.");
    second.reset();
    Require(budget->Reserved() == 12, "One shared owner prematurely released the reservation.");
    copy.reset();
    Require(budget->Reserved() == 4 && budget->Peak() == 12, "The last owner did not release its reservation.");
    first.reset();
    Require(budget->Reserved() == 0, "A completed request leaked reservations.");
}

void FailedReplacementRetainsOldAndNewUntilRecovery()
{
    const auto budget = std::make_shared<HdSilkMeshPreparationBudget>();
    budget->Configure(50);
    auto old = budget->Acquire(20);
    auto next = budget->Acquire(30);
    next->RetainPrevious(old);
    old.reset();
    Require(budget->Reserved() == 50, "A pending replacement released old storage too soon.");
    next.reset();
    Require(budget->Reserved() == 0, "Aborting the owner did not release both generations.");
    old = budget->Acquire(20);
    next = budget->Acquire(30);
    next->RetainPrevious(old);
    old.reset();
    next->Complete();
    Require(budget->Reserved() == 30, "Completed replacement retained the old generation.");
    bool failed = false;
    try { budget->Configure(29); }
    catch (const HdSilkMeshPreparationExceeded&) { failed = true; }
    Require(failed && budget->Limit() == 50 && budget->Reserved() == 30, "A rejected ceiling changed live state.");
}

void ConcurrentReservationsCannotOvercommit()
{
    const auto budget = std::make_shared<HdSilkMeshPreparationBudget>();
    budget->Configure(64);
    std::array<std::shared_ptr<HdSilkMeshPreparationLease>, 8> leases;
    std::array<std::thread, 8> threads;
    std::atomic<unsigned> refusals{0};
    for (size_t index = 0; index < threads.size(); ++index)
    {
        threads[index] = std::thread([&, index]
        {
            try { leases[index] = budget->Acquire(16); }
            catch (const HdSilkMeshPreparationExceeded&) { ++refusals; }
        });
    }
    for (auto& thread : threads) { thread.join(); }
    Require(refusals == 4 && budget->Reserved() == 64 && budget->Peak() == 64,
        "Concurrent workers overcommitted or lost an admitted reservation.");
    for (auto& lease : leases) { lease.reset(); }
    Require(budget->Reserved() == 0, "Concurrent completion leaked reservations.");
}

void WorkerRefusalIsPublishedOnTheCallerAndResetForRetry()
{
    const auto budget = std::make_shared<HdSilkMeshPreparationBudget>();
    budget->Configure(64);
    std::thread worker([&] { budget->Refuse("first preparation refusal"); });
    worker.join();
    budget->Refuse("later refusal");
    bool failed = false;
    try { budget->ThrowIfRefused(); }
    catch (const HdSilkMeshPreparationExceeded& error)
    {
        failed = std::string(error.what()) == "first preparation refusal";
    }
    Require(failed, "The caller lost or replaced the worker's original refusal.");
    budget->Configure(64);
    budget->ThrowIfRefused();
    Require(!budget->Refused() && budget->Reserved() == 0, "Retry retained the failed request state.");
}
}

int main()
{
    try
    {
        ReservationArithmeticIsExactAndRejectsOverflow();
        LimitsAndSharedOwnersPreserveExactBoundaries();
        FailedReplacementRetainsOldAndNewUntilRecovery();
        ConcurrentReservationsCannotOvercommit();
        WorkerRefusalIsPublishedOnTheCallerAndResetForRetry();
        std::cout << "hdSilk mesh preparation: 5 reservation contracts passed\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
